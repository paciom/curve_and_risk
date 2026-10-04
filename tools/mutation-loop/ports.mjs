// The mutation loop's contact with the outside world: Stryker, the coding agent, git and the ledger.
// loop.mjs holds the decisions and is tested against scripted versions of these.
//
// The loop remembers the last state it accepted as a git tree. "Changed" means different from that
// tree in the working tree or in the index, so staging a file does not hide it; "kept" means staged
// and made the new accepted tree; "reverted" means index and working tree put back to it. A commit
// made during an attempt, or a revert that does not take, aborts the run.

import { spawnSync } from "node:child_process";
import { appendFileSync, existsSync, mkdirSync, readFileSync, rmSync } from "node:fs";
import path from "node:path";

const OUTPUT_TAIL = 3000;
const AGENT_TIMEOUT_MS = 30 * 60_000;
const STRYKER_TIMEOUT_MS = 60 * 60_000;
const MAX_BUFFER = 64 * 1024 * 1024;

function run(root, command, args, options = {}) {
  const result = spawnSync(command, args, { cwd: root, encoding: "utf8", maxBuffer: MAX_BUFFER, ...options });
  const output = `${result.stdout ?? ""}\n${result.stderr ?? ""}`.trim();
  return { ok: result.status === 0, stdout: result.stdout ?? "", output: output.slice(-OUTPUT_TAIL), timedOut: result.error?.code === "ETIMEDOUT" };
}

function gitCommand(root) {
  return args => {
    const result = run(root, "git", ["-c", "core.quotePath=false", ...args]);
    if (!result.ok) throw new Error(`git ${args[0]} failed: ${result.output}`);
    return result.stdout;
  };
}

const records = text => text.split("\0").filter(Boolean);

/** Parses `git diff --numstat -z`: [file, lines removed]. A binary file has no count and is treated as rewritten. */
const parseNumstat = text => records(text).map(record => {
  const [, removed, file] = record.split("\t");
  return [file, removed === "-" ? 1 : Number(removed)];
});

function gitPorts(root) {
  const git = gitCommand(root);
  let accepted = null;
  const snapshot = () => { accepted = { head: git(["rev-parse", "HEAD"]).trim(), tree: git(["write-tree"]).trim() }; };
  const untracked = () => records(git(["ls-files", "--others", "--exclude-standard", "-z"]));
  const numstat = scope => parseNumstat(git(["diff", ...scope, "--numstat", "--no-renames", "-z", accepted.tree]));

  function changes() {
    if (git(["rev-parse", "HEAD"]).trim() !== accepted.head) throw new Error("A commit was made during an attempt. The loop only stages; stopping so a person can look.");
    const removedByFile = new Map(untracked().map(file => [file, 0]));
    for (const [file, removed] of [...numstat([]), ...numstat(["--cached"])]) {
      removedByFile.set(file, Math.max(removedByFile.get(file) ?? 0, removed));
    }
    return [...removedByFile].map(([file, removedLines]) => ({ file, removedLines }));
  }

  function revert() {
    git(["read-tree", accepted.tree]);
    git(["checkout-index", "-a", "-f"]);
    for (const file of untracked()) rmSync(path.join(root, file), { force: true });
    const left = changes();
    if (left.length > 0) throw new Error(`Revert left changes behind: ${left.map(change => change.file).join(", ")}`);
  }

  return {
    isClean: () => git(["status", "--porcelain"]).trim() === "",
    begin: snapshot,
    changes,
    revert,
    contentOf: file => (existsSync(path.join(root, file)) ? readFileSync(path.join(root, file), "utf8") : ""),
    /** Stages exactly the files that were checked, then drops anything else that appeared since. */
    keep(files) {
      git(["add", "--", ...files]);
      snapshot();
      revert();
    },
    /** The ledger is part of the accepted tree, so a revert never removes it and an agent edit to it shows as a change. */
    acceptLedger(ledger) {
      git(["add", "--", ledger]);
      snapshot();
    },
  };
}

/** Runs Stryker on one file, or on everything when no file is given, and returns its JSON report. */
function strykerPort(root) {
  let runs = 0;
  return file => {
    runs += 1;
    const outputDir = path.join(root, "StrykerOutput", "loop", String(runs));
    rmSync(outputDir, { recursive: true, force: true });
    const scope = file ? ["--mutate", `**/${path.posix.basename(file)}`] : [];
    const result = run(root, "dotnet", ["stryker", "-O", outputDir, ...scope], { timeout: STRYKER_TIMEOUT_MS });
    if (result.timedOut) throw new Error("Stryker timed out; its processes may still be running.");
    const reportPath = path.join(outputDir, "reports", "mutation-report.json");
    if (!result.ok || !existsSync(reportPath)) return { ok: false, output: result.output };
    return { ok: true, report: JSON.parse(readFileSync(reportPath, "utf8")) };
  };
}

/**
 * Turns the CLI's result into a reply with its cost in US dollars. A reply that cannot be read is
 * charged the whole budget it was given: the spend cap must hold even when the cost is unknown.
 */
export function parseAgentReply(result, budgetUsd) {
  try {
    const reply = JSON.parse(result.stdout);
    return { ok: result.ok && !reply.is_error, costUsd: reply.total_cost_usd ?? budgetUsd, text: String(reply.result ?? "") };
  } catch {
    return { ok: false, costUsd: budgetUsd, text: result.output };
  }
}

/**
 * One headless Claude Code session in the repository. The prompt goes in on stdin; the project's own
 * settings decide which commands it may run, and its hooks (protected paths, format, verify on stop)
 * apply to it as they do to any session. The CLI enforces the budget it is given.
 */
function agentPort(root) {
  return (prompt, budgetUsd) => {
    const args = ["-p", "--output-format", "json", "--permission-mode", "acceptEdits", "--max-budget-usd", budgetUsd.toFixed(2)];
    const result = run(root, "claude", args, { input: prompt, timeout: AGENT_TIMEOUT_MS, shell: process.platform === "win32" });
    // On Windows the timeout ends the shell, not the session under it, which could go on editing the tree.
    if (result.timedOut) throw new Error("The agent timed out and may still be running. Check for a stray claude process before running the loop again.");
    return parseAgentReply(result, budgetUsd);
  };
}

function ledgerPort(root, ledger, accept) {
  const ledgerPath = path.join(root, ledger);
  return entry => {
    mkdirSync(path.dirname(ledgerPath), { recursive: true });
    appendFileSync(ledgerPath, `${JSON.stringify({ at: new Date().toISOString(), ...entry })}\n`);
    accept(ledger);
  };
}

export function createPorts(root, settings) {
  const { acceptLedger, ...git } = gitPorts(root);
  return {
    ...git,
    measure: strykerPort(root),
    askAgent: agentPort(root),
    fastGate: () => run(root, "node", ["tools/check.mjs", "--fast"]),
    record: ledgerPort(root, settings.ledger, acceptLedger),
  };
}
