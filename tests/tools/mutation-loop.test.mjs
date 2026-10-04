// Tests for the mutation-kill loop. Run with: node --test "tests/tools/*.test.mjs"
// The loop runs for real; only its ports (Stryker, the agent, git, the ledger) are scripted, so each
// test states what the agent left behind and what Stryker then measured, and asserts what the loop did.

import { test } from "node:test";
import assert from "node:assert/strict";
import { runLoop } from "../../tools/mutation-loop/loop.mjs";
import { readMutants, score, targetsByFile } from "../../tools/mutation-loop/report.mjs";

const SOURCE = ["if (x >= 1) return;", "await task.ConfigureAwait(false);", "return a + b;"].join("\n");
const SHAPES = [
  { mutatorName: "Equality mutation", replacement: "x > 1", start: 5, end: 11 },
  { mutatorName: "Boolean mutation", replacement: "true", start: 27, end: 32 },
  { mutatorName: "Arithmetic mutation", replacement: "a - b", start: 8, end: 13 },
];

/** A Stryker report: for each file, the status of its three mutants (comparison, ConfigureAwait, arithmetic). */
function reportOf(files) {
  const entries = Object.entries(files).map(([file, statuses]) => [`D:\\repo\\${file.replaceAll("/", "\\")}`, {
    source: SOURCE,
    mutants: statuses.map((status, index) => ({
      id: String(index), mutatorName: SHAPES[index].mutatorName, replacement: SHAPES[index].replacement, status,
      location: { start: { line: index + 1, column: SHAPES[index].start }, end: { line: index + 1, column: SHAPES[index].end } },
    })),
  }]);
  return { projectRoot: "D:\\repo", files: Object.fromEntries(entries) };
}

const settings = {
  targetScorePercent: 100, maxRounds: 10, maxAttemptsPerFile: 2, stallRounds: 2, maxSpendUsd: 10, maxSpendPerAttemptUsd: 3,
  allowedPaths: ["^tests/CurveRisk\\.[^/]+/.+\\.cs$"], protectedPaths: ["/golden/", "\\.verified\\."],
  forbiddenTestContent: ["Stryker", "GetEnvironmentVariable"],
  unobservable: [{ precededBy: "ConfigureAwait(" }],
};
const TEST_FILE = "tests/CurveRisk.Api.Tests/ATests.cs";

/** Ports that replay one scripted attempt per agent call and log every call the loop makes. */
function scripted(baseline, attempts) {
  const calls = [];
  const ledger = [];
  let current = null;
  let measured = false;
  const changes = () => (measured && current.lateChanged ? current.lateChanged : current.changed)
    .map(file => ({ file, removedLines: current.removed?.[file] ?? 0 }));
  const ports = {
    baseline: () => baseline,
    askAgent(prompt, budgetUsd) {
      if (current?.agentThrows) throw new Error(current.agentThrows);
      current = attempts.shift() ?? { changed: [] };
      measured = false;
      calls.push({ call: "askAgent", prompt, budgetUsd });
      return { ok: current.agentOk ?? true, costUsd: current.costUsd ?? 1, text: current.says ?? "" };
    },
    changes,
    contentOf: () => current.content ?? "Assert.Equal(1, 1);",
    fastGate: () => (calls.push({ call: "fastGate" }), { ok: current.gateOk ?? true, output: "size: too long" }),
    measure: file => (measured = true, calls.push({ call: "measure", file }), current.measured ? { ok: true, report: current.measured } : { ok: false, output: "1 test failed" }),
    keep: files => calls.push({ call: "keep", files }),
    revert: () => calls.push({ call: "revert" }),
    record: entry => ledger.push(entry),
  };
  const named = name => calls.filter(c => c.call === name);
  return { ports, ledger, named, attemptsLogged: () => ledger.filter(e => e.event === "attempt") };
}

test("the report is read into repository-relative mutants with the text they replace", () => {
  const [comparison, configureAwait] = readMutants(reportOf({ "src/A.cs": ["Survived", "Survived", "Killed"] }));

  assert.equal(comparison.file, "src/A.cs");
  assert.equal(comparison.original, "x >= 1");
  assert.equal(comparison.key, "src/A.cs:1:5:Equality mutation:x > 1");
  assert.equal(configureAwait.before, "await task.ConfigureAwait(");
});

test("the score counts timeouts as detected and no-coverage as undetected, and ignores the rest", () => {
  const mutants = readMutants(reportOf({ "src/A.cs": ["Timeout", "NoCoverage", "Ignored"], "src/B.cs": ["Killed", "Survived", "CompileError"] }));

  assert.deepEqual(score(mutants), { detected: 2, undetected: 2 });
});

test("targets leave out detected and unobservable mutants, and the file with most survivors comes first", () => {
  const mutants = readMutants(reportOf({ "src/A.cs": ["Survived", "Survived", "Killed"], "src/B.cs": ["Survived", "Killed", "Survived"] }));

  const queue = targetsByFile(mutants, settings.unobservable);

  assert.deepEqual(queue.map(entry => [entry.file, entry.targets.length]), [["src/B.cs", 2], ["src/A.cs", 1]]);
});

test("a test change that Stryker confirms as a kill is kept and the loop stops at the target", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [
    { changed: [TEST_FILE], measured: reportOf({ "src/A.cs": ["Killed", "Killed", "Killed"] }) },
  ]);

  const summary = runLoop(settings, run.ports);

  assert.equal(summary.reason, "target-reached");
  assert.equal(summary.scorePercent, 100);
  assert.deepEqual(run.named("keep").map(c => c.files), [[TEST_FILE]]);
  assert.equal(run.named("revert").length, 0);
  assert.deepEqual(run.named("measure").map(c => c.file), ["src/A.cs"]);
  assert.deepEqual(run.attemptsLogged()[0].killed, ["src/A.cs:1:5:Equality mutation:x > 1"]);
});

test("the agent's own claim counts for nothing: a mutant Stryker still reports surviving is reverted", () => {
  const stillSurviving = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(stillSurviving, [
    { changed: [TEST_FILE], measured: stillSurviving, says: "All mutants are now killed." },
    { changed: [TEST_FILE], measured: stillSurviving, says: "Killed." },
  ]);

  const summary = runLoop(settings, run.ports);

  assert.equal(summary.reason, "no-targets");
  assert.equal(summary.scorePercent, summary.startPercent);
  assert.equal(run.named("keep").length, 0);
  assert.equal(run.named("revert").length, 2);
  assert.deepEqual(run.attemptsLogged().map(e => e.outcome), ["nothing-killed", "nothing-killed"]);
});

test("a change outside the test projects is reverted without being measured", () => {
  for (const forbidden of ["src/CurveRisk.Api/A.cs", "tests/CurveRisk.Api.Tests/golden/a.json", "stryker-config.json", "tests/hooks/x.test.mjs"]) {
    const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [{ changed: [TEST_FILE, forbidden] }]);

    runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

    assert.equal(run.attemptsLogged()[0].outcome, "out-of-bounds", forbidden);
    assert.equal(run.named("measure").length, 0, forbidden);
    assert.equal(run.named("revert").length, 1, forbidden);
  }
});

test("a failing quality gate or a failing Stryker run reverts the attempt", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [
    { changed: [TEST_FILE], gateOk: false },
    { changed: [TEST_FILE] },
  ]);

  runLoop(settings, run.ports);

  assert.deepEqual(run.attemptsLogged().map(e => e.outcome), ["gate-failed", "measure-failed"]);
  assert.equal(run.named("measure").length, 1);
  assert.equal(run.named("revert").length, 2);
});

test("the second attempt is told what the check found, and only about mutants that still survive", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Survived"] }), [
    { changed: [TEST_FILE], measured: reportOf({ "src/A.cs": ["Killed", "Killed", "Survived"] }) },
    { changed: [] },
  ]);

  runLoop(settings, run.ports);

  const [first, second] = run.named("askAgent").map(c => c.prompt);
  assert.match(first, /x >= 1/);
  assert.doesNotMatch(first, /previous attempt/);
  assert.doesNotMatch(second, /x >= 1/);
  assert.match(second, /a \+ b/);
  assert.match(second, /previous attempt[^]*Kept: 1 killed/);
  assert.deepEqual(run.attemptsLogged().map(e => e.outcome), ["killed", "no-change"]);
});

test("the loop stops after the configured number of rounds without a kill", () => {
  const baseline = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"], "src/B.cs": ["Survived", "Killed", "Killed"], "src/C.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(baseline, []);

  const summary = runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

  assert.equal(summary.reason, "stalled");
  assert.equal(summary.rounds, 2);
  assert.equal(summary.filesNotTried, 1);
});

test("the loop stops at the round limit", () => {
  const baseline = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"], "src/B.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(baseline, []);

  const summary = runLoop({ ...settings, maxRounds: 1 }, run.ports);

  assert.equal(summary.reason, "round-limit");
  assert.equal(run.named("askAgent").length, 2);
});

test("spend is capped: each call gets at most what is left, and no call is made once it is gone", () => {
  const baseline = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"], "src/B.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(baseline, [{ changed: [], costUsd: 3 }, { changed: [], costUsd: 1 }]);

  const summary = runLoop({ ...settings, maxSpendUsd: 4 }, run.ports);

  assert.equal(summary.reason, "spend-limit");
  assert.deepEqual(run.named("askAgent").map(c => c.budgetUsd), [3, 1]);
  assert.equal(summary.spendUsd, 4);
});

test("nothing is asked of the agent when the starting score already meets the target", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Killed", "Survived", "Killed"] }), []);

  const summary = runLoop({ ...settings, targetScorePercent: 60 }, run.ports);

  assert.equal(summary.reason, "target-reached");
  assert.equal(run.named("askAgent").length, 0);
  assert.deepEqual(run.ledger.map(e => e.event), ["start", "stop"]);
});

test("a kill bought by letting a previously killed mutant survive is reverted", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [
    { changed: [TEST_FILE], measured: reportOf({ "src/A.cs": ["Killed", "Killed", "Survived"] }) },
  ]);

  const summary = runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

  assert.equal(run.attemptsLogged()[0].outcome, "regressed");
  assert.equal(summary.scorePercent, summary.startPercent);
  assert.equal(run.named("keep").length, 0);
  assert.equal(run.named("revert").length, 1);
});

test("removing a line of an existing test is reverted without being measured", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [{ changed: [TEST_FILE], removed: { [TEST_FILE]: 1 } }]);

  runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

  assert.equal(run.attemptsLogged()[0].outcome, "tests-removed");
  assert.equal(run.named("measure").length, 0);
});

test("a test that reads the environment to detect the mutation run is reverted", () => {
  const content = 'if (Environment.GetEnvironmentVariable("ActiveMutation") != null) Assert.Fail();';
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [{ changed: [TEST_FILE], content }]);

  runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

  assert.equal(run.attemptsLogged()[0].outcome, "forbidden-content");
  assert.equal(run.named("measure").length, 0);
});

test("a file that appears while the tests run is caught before anything is kept", () => {
  const killed = reportOf({ "src/A.cs": ["Killed", "Killed", "Killed"] });
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"] }), [
    { changed: [TEST_FILE], lateChanged: [TEST_FILE, "src/CurveRisk.Api/A.cs"], measured: killed },
  ]);

  runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports);

  assert.equal(run.attemptsLogged()[0].outcome, "out-of-bounds");
  assert.equal(run.named("keep").length, 0);
  assert.equal(run.named("revert").length, 1);
});

test("no further attempt is paid for once the target is reached mid-round", () => {
  const run = scripted(reportOf({ "src/A.cs": ["Survived", "Killed", "Survived"] }), [
    { changed: [TEST_FILE], measured: reportOf({ "src/A.cs": ["Killed", "Killed", "Survived"] }) },
  ]);

  const summary = runLoop({ ...settings, targetScorePercent: 60 }, run.ports);

  assert.equal(summary.reason, "target-reached");
  assert.equal(run.named("askAgent").length, 1);
});

test("a remainder too small to be useful counts as the spend limit", () => {
  const baseline = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"], "src/B.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(baseline, [{ changed: [], costUsd: 3 }]);

  const summary = runLoop({ ...settings, maxSpendUsd: 3.1 }, run.ports);

  assert.equal(summary.reason, "spend-limit");
  assert.equal(run.named("askAgent").length, 1);
});

test("a port that throws aborts the run, and the ledger still gets its closing entry", () => {
  const baseline = reportOf({ "src/A.cs": ["Survived", "Killed", "Killed"], "src/B.cs": ["Survived", "Killed", "Killed"] });
  const run = scripted(baseline, [{ changed: [], agentThrows: "The agent timed out" }]);

  assert.throws(() => runLoop({ ...settings, maxAttemptsPerFile: 1 }, run.ports), /timed out/);

  const last = run.ledger.at(-1);
  assert.equal(last.reason, "aborted");
  assert.match(last.error, /timed out/);
});
