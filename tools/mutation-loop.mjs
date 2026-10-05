#!/usr/bin/env node
// Mutation-kill loop: directs a coding agent to kill surviving mutants, and checks every attempt itself.
//
//   node tools/mutation-loop.mjs --dry-run --report <mutation-report.json>   show the plan; calls nothing
//   node tools/mutation-loop.mjs --report <mutation-report.json>             start from an existing full report
//   node tools/mutation-loop.mjs                                             measure first (a full Stryker run)
//   ... --max-rounds 3 --max-spend-usd 5                                     tighter limits for one run
//
// A live run makes model calls through the `claude` CLI and costs money, up to maxSpendUsd. Limits,
// the target score and the paths the agent may change are in tools/quality.config.json under
// "mutationLoop". Each attempt is appended to the ledger named there. Kept tests are staged, not
// committed: review and commit them yourself. Leave this checkout alone while it runs: no builds, no
// edits. The loop stops if it finds a change that is not its own, and sets rejected work aside under
// .git/mutation-loop-rejected rather than deleting it.
//
// The loop's score is the starting report plus the kills Stryker confirmed file by file. The score
// of record is still a full `dotnet stryker` run afterwards.

import { existsSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { parseArgs } from "node:util";
import { runLoop, stopReason } from "./mutation-loop/loop.mjs";
import { createPorts } from "./mutation-loop/ports.mjs";
import { buildPrompt } from "./mutation-loop/prompt.mjs";
import { percent, readMutants, score, targetsByFile } from "./mutation-loop/report.mjs";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const config = JSON.parse(readFileSync(path.join(root, "tools", "quality.config.json"), "utf8")).mutationLoop;

const { values: options } = parseArgs({
  options: {
    report: { type: "string" },
    "dry-run": { type: "boolean", default: false },
    "max-rounds": { type: "string" },
    "max-spend-usd": { type: "string" },
  },
});
function fail(message) {
  console.error(message);
  process.exit(1);
}

function limit(name, fallback) {
  const value = Number(options[name] ?? fallback);
  return Number.isFinite(value) && value > 0 ? value : fail(`--${name} must be a positive number.`);
}
const settings = { ...config, maxRounds: limit("max-rounds", config.maxRounds), maxSpendUsd: limit("max-spend-usd", config.maxSpendUsd) };

/** A report made from different source gives line numbers and mutants that no longer match; refuse it. */
function staleFiles(report) {
  const sameText = (a, b) => a.replaceAll("\r\n", "\n") === b.replaceAll("\r\n", "\n");
  return Object.entries(report.files)
    .filter(([file, entry]) => !existsSync(file) || !sameText(entry.source ?? "", readFileSync(file, "utf8")))
    .map(([file]) => path.relative(root, file));
}

function dryRun(report) {
  const mutants = readMutants(report);
  const queue = targetsByFile(mutants, settings.unobservable);
  const state = { ...score(mutants), queue, rounds: 0, spendUsd: 0, stalledRounds: 0 };
  console.log(`Score ${percent(state).toFixed(2)}%, target ${settings.targetScorePercent}%. ${state.undetected} undetected, of which the loop will try:`);
  for (const { file, targets } of queue) console.log(`  ${String(targets.length).padStart(3)}  ${file}`);
  console.log(`Would stop now: ${stopReason(state, settings) ?? "no"}`);
  if (queue.length > 0) console.log(`\nFirst prompt:\n\n${buildPrompt(queue[0].file, queue[0].targets, null)}`);
}

const ports = createPorts(root, settings);
function readReport() {
  const report = JSON.parse(readFileSync(path.resolve(options.report), "utf8"));
  const stale = staleFiles(report);
  return stale.length === 0 ? report : fail(`The report does not match the code. Run without --report to measure afresh. Changed since: ${stale.join(", ")}`);
}

if (options["dry-run"]) {
  if (!options.report) fail("--dry-run needs --report <mutation-report.json>.");
  dryRun(readReport());
} else {
  if (!ports.isClean()) fail("The working tree has uncommitted changes. Commit or stash them first: the loop reverts whatever it does not keep.");
  const baseline = () => {
    if (options.report) return readReport();
    const measured = ports.measure(null);
    return measured.ok ? measured.report : fail(`The first Stryker run failed.\n${measured.output}`);
  };
  ports.begin();
  const summary = runLoop(settings, { ...ports, baseline });
  console.log(`Stopped: ${summary.reason}. Score ${summary.startPercent}% -> ${summary.scorePercent}% in ${summary.rounds} round(s), $${summary.spendUsd} spent.`);
  console.log(`Ledger: ${settings.ledger}. Kept tests are staged; confirm the score with a full 'dotnet stryker' run.`);
}
