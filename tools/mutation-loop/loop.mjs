// The mutation-kill loop: the script directs the coding agent, checks its work and decides what is kept.
//
//   measure -> pick a file's surviving mutants -> prompt the agent -> check -> keep or revert -> repeat
//
// The agent is never trusted about its own result. After every attempt the loop itself checks that
// tests were only added (nothing outside the test projects touched, no existing line removed), that
// the fast quality gate passes, and that Stryker, run again on that file, reports the targets killed
// and nothing that was detected before now surviving. Anything else is reverted and the reason is
// fed back as the next instruction.
//
// The loop stops on the first of: target score reached, nothing left to try, round limit, spend
// limit, or several rounds in a row without a kill. Everything that touches the outside world comes
// in through `ports`, so the whole loop runs in tests against a scripted agent.

import { buildPrompt } from "./prompt.mjs";
import { isDetected, percent, readMutants, score, splitByOutcome, targetsByFile } from "./report.mjs";

const NOTE_LENGTH = 600;
/** Below this many US dollars an attempt cannot do useful work, so the budget counts as spent. */
const MIN_ATTEMPT_USD = 0.25;

const rounded = value => Number(value.toFixed(2));
const matchesAny = (text, patterns) => patterns.some(pattern => new RegExp(pattern, "i").test(text));
const budgetLeftUsd = (state, settings) => settings.maxSpendUsd - state.spendUsd;

/** Why the loop must stop now, or null to run another round. The order is the order of precedence. */
export function stopReason(state, settings) {
  if (percent(state) >= settings.targetScorePercent) return "target-reached";
  if (state.queue.length === 0) return "no-targets";
  if (state.rounds >= settings.maxRounds) return "round-limit";
  if (budgetLeftUsd(state, settings) < MIN_ATTEMPT_USD) return "spend-limit";
  if (state.stalledRounds >= settings.stallRounds) return "stalled";
  return null;
}

/** What is wrong with the set of changed files, as [outcome, feedback], or null when tests were only added. */
function boundsProblem(changes, { ports, settings }) {
  const isAllowed = file => matchesAny(file, settings.allowedPaths) && !matchesAny(file, settings.protectedPaths);
  const outside = changes.filter(change => !isAllowed(change.file)).map(change => change.file);
  if (outside.length > 0) return ["out-of-bounds", `Only test files may change. Everything was reverted because you changed: ${outside.join(", ")}`];

  const rewritten = changes.filter(change => change.removedLines > 0).map(change => change.file);
  if (rewritten.length > 0) return ["tests-removed", `Tests may be added, never changed or removed. Reverted because existing lines were removed in: ${rewritten.join(", ")}`];

  const forbidden = changes.filter(change => matchesAny(ports.contentOf(change.file), settings.forbiddenTestContent)).map(change => change.file);
  if (forbidden.length > 0) return ["forbidden-content", `Reverted: a test must not read the environment or detect the mutation run. See: ${forbidden.join(", ")}`];
  return null;
}

/** What Stryker says about the attempt, as [outcome, feedback] when it must be rejected, else the kills. */
function measureProblem(file, targets, { ports, state }) {
  const measured = ports.measure(file);
  if (!measured.ok) return { problem: ["measure-failed", `Reverted: Stryker could not run, usually because the build or a test fails on the unmodified code.\n${measured.output}`] };

  const mutants = readMutants(measured.report);
  const regressed = mutants.filter(mutant => state.detectedKeys.has(mutant.key) && !isDetected(mutant));
  if (regressed.length > 0) return { problem: ["regressed", `Reverted: ${regressed.length} mutant(s) that were killed before your change now survive.`] };

  const split = splitByOutcome(targets, mutants);
  if (split.killed.length === 0) return { problem: ["nothing-killed", "Reverted: the tests passed, but Stryker reports every listed mutant still surviving."] };
  return split;
}

/** The loop's own verdict on what the agent left in the working tree. */
function judge(file, targets, context) {
  const { ports } = context;
  const changes = ports.changes();
  const files = changes.map(change => change.file);
  const reject = ([outcome, feedback]) => ({ keep: false, outcome, feedback, changed: files, killed: [], remaining: targets });

  if (changes.length === 0) return reject(["no-change", "You changed no files."]);
  const bounds = boundsProblem(changes, context);
  if (bounds) return reject(bounds);
  const gate = ports.fastGate();
  if (!gate.ok) return reject(["gate-failed", `Reverted: the quality gate failed.\n${gate.output}`]);
  const measured = measureProblem(file, targets, context);
  if (measured.problem) return reject(measured.problem);

  // The gate and Stryker run the agent's code, which could have written files; check again what is about to be kept.
  const final = ports.changes();
  const late = boundsProblem(final, context);
  if (late) return reject(late);
  const { killed, remaining } = measured;
  return { keep: true, outcome: "killed", changed: final.map(change => change.file), killed, remaining, feedback: `Kept: ${killed.length} killed. The mutants listed above still survive.` };
}

function attempt(step, context) {
  const { ports, settings, state } = context;
  const budgetUsd = Math.min(settings.maxSpendPerAttemptUsd, budgetLeftUsd(state, settings));
  const reply = ports.askAgent(buildPrompt(step.file, step.targets, step.feedback), budgetUsd);
  state.spendUsd += reply.costUsd;

  const verdict = judge(step.file, step.targets, context);
  if (verdict.keep) ports.keep(verdict.changed);
  else ports.revert();

  for (const mutant of verdict.killed) state.detectedKeys.add(mutant.key);
  state.detected += verdict.killed.length;
  state.undetected -= verdict.killed.length;
  ports.record({
    event: "attempt", round: state.rounds, attempt: step.number, file: step.file,
    targets: step.targets.length, outcome: verdict.outcome, killed: verdict.killed.map(m => m.key),
    remaining: verdict.remaining.length, changed: verdict.changed, agentOk: reply.ok,
    costUsd: rounded(reply.costUsd), spendUsd: rounded(state.spendUsd), scorePercent: rounded(percent(state)),
    note: reply.text.slice(-NOTE_LENGTH),
  });
  return verdict;
}

/** One file: up to maxAttemptsPerFile attempts, each told what the check found wrong with the last. */
function runRound(context) {
  const { settings, state } = context;
  const { file, targets } = state.queue.shift();
  state.rounds += 1;
  const mayContinue = step =>
    step.number <= settings.maxAttemptsPerFile && step.targets.length > 0
    && budgetLeftUsd(state, settings) >= MIN_ATTEMPT_USD && percent(state) < settings.targetScorePercent;

  let step = { file, targets, feedback: null, number: 1 };
  let killed = 0;
  while (mayContinue(step)) {
    const verdict = attempt(step, context);
    killed += verdict.killed.length;
    step = { file, targets: verdict.remaining, feedback: verdict.feedback, number: step.number + 1 };
  }
  state.stalledRounds = killed > 0 ? 0 : state.stalledRounds + 1;
}

function initialState(mutants, settings) {
  return {
    ...score(mutants),
    detectedKeys: new Set(mutants.filter(isDetected).map(mutant => mutant.key)),
    queue: targetsByFile(mutants, settings.unobservable),
    rounds: 0, spendUsd: 0, stalledRounds: 0,
  };
}

/**
 * Runs the loop to a stop condition and returns why it stopped and where it got to. A port that
 * throws (a timed-out agent, a revert that did not take) aborts the run; the ledger still gets its
 * closing entry, and the error is rethrown.
 */
export function runLoop(settings, ports) {
  const state = initialState(readMutants(ports.baseline()), settings);
  const startPercent = rounded(percent(state));
  const targets = state.queue.reduce((sum, entry) => sum + entry.targets.length, 0);
  ports.record({ event: "start", scorePercent: startPercent, targets, files: state.queue.length, targetScorePercent: settings.targetScorePercent });
  const stop = (reason, extra = {}) => {
    const summary = { event: "stop", reason, rounds: state.rounds, startPercent, scorePercent: rounded(percent(state)), spendUsd: rounded(state.spendUsd), filesNotTried: state.queue.length, ...extra };
    ports.record(summary);
    return summary;
  };

  try {
    let reason = stopReason(state, settings);
    while (!reason) {
      runRound({ settings, ports, state });
      reason = stopReason(state, settings);
    }
    return stop(reason);
  } catch (error) {
    stop("aborted", { error: error.message });
    throw error;
  }
}
