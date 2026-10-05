// Test support for the mutation-kill loop: Stryker reports built from a few statuses, and ports that
// replay a scripted agent. Not a test file; mutation-loop.test.mjs uses it.

const SOURCE = ["if (x >= 1) return;", "await task.ConfigureAwait(false);", "return a + b;"].join("\n");
const SHAPES = [
  { mutatorName: "Equality mutation", replacement: "x > 1", start: 5, end: 11 },
  { mutatorName: "Boolean mutation", replacement: "true", start: 27, end: 32 },
  { mutatorName: "Arithmetic mutation", replacement: "a - b", start: 8, end: 13 },
];

/** A Stryker report: for each file, the status of its three mutants (comparison, ConfigureAwait, arithmetic). */
export function reportOf(files) {
  const entries = Object.entries(files).map(([file, statuses]) => [`D:\\repo\\${file.replaceAll("/", "\\")}`, {
    source: SOURCE,
    mutants: statuses.map((status, index) => ({
      id: String(index), mutatorName: SHAPES[index].mutatorName, replacement: SHAPES[index].replacement, status,
      location: { start: { line: index + 1, column: SHAPES[index].start }, end: { line: index + 1, column: SHAPES[index].end } },
    })),
  }]);
  return { projectRoot: "D:\\repo", files: Object.fromEntries(entries) };
}

export const settings = {
  targetScorePercent: 100, maxRounds: 10, maxAttemptsPerFile: 2, stallRounds: 2, maxSpendUsd: 10, maxSpendPerAttemptUsd: 3,
  allowedPaths: ["^tests/CurveRisk\\.[^/]+/.+\\.cs$"], protectedPaths: ["/golden/", "\\.verified\\."],
  forbiddenTestContent: ["Stryker", "GetEnvironmentVariable"],
  unobservable: [{ precededBy: "ConfigureAwait(" }],
};
export const TEST_FILE = "tests/CurveRisk.Api.Tests/ATests.cs";

/** Ports that replay one scripted attempt per agent call and log every call the loop makes. */
export function scripted(baseline, attempts) {
  const calls = [];
  const ledger = [];
  let [current, measured, pending] = [null, false, false];
  const settle = call => { pending = false; calls.push(call); };
  const filesNow = () => {
    if (!pending) return attempts[0]?.foreign ?? [];
    return measured && current.lateChanged ? current.lateChanged : current.changed;
  };
  const changes = () => filesNow().map(file => ({ file, removedLines: current?.removed?.[file] ?? 0 }));
  const ports = {
    baseline: () => baseline,
    askAgent(prompt, budgetUsd) {
      if (current?.agentThrows) throw new Error(current.agentThrows);
      current = attempts.shift() ?? { changed: [] };
      measured = false;
      pending = true;
      calls.push({ call: "askAgent", prompt, budgetUsd });
      return { ok: current.agentOk ?? true, costUsd: current.costUsd ?? 1, text: current.says ?? "" };
    },
    changes,
    contentOf: () => current.content ?? "Assert.Equal(1, 1);",
    fastGate: () => (calls.push({ call: "fastGate" }), { ok: current.gateOk ?? true, output: "size: too long" }),
    measure: file => (measured = true, calls.push({ call: "measure", file }), current.measured ? { ok: true, report: current.measured } : { ok: false, output: "1 test failed" }),
    keep: files => settle({ call: "keep", files }),
    revert: () => settle({ call: "revert" }),
    record: entry => ledger.push(entry),
  };
  const named = name => calls.filter(c => c.call === name);
  return { ports, ledger, named, attemptsLogged: () => ledger.filter(e => e.event === "attempt") };
}
