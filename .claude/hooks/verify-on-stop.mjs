#!/usr/bin/env node
// Stop hook.
//
// The agent may not end its turn on code that does not build or whose tests fail. If source files
// changed since the last verified state, this builds (warnings are errors) and runs the test suite;
// on failure it exits 2, which keeps the agent working with the failure output in front of it.
//
// "Done" is therefore something the harness checks rather than something the agent asserts.

import { readFileSync, writeFileSync, existsSync, statSync } from "node:fs";
import { spawnSync } from "node:child_process";
import { createHash } from "node:crypto";
import path from "node:path";

let input = {};
try {
  input = JSON.parse(readFileSync(0, "utf8"));
} catch {
  process.exit(0);
}

// Set when the agent is already continuing because of this hook. Without this check a failure the
// agent cannot fix would loop forever; with it, the second stop goes through and the user sees the state.
if (input.stop_hook_active) process.exit(0);

const projectDir = process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd();
if (!existsSync(path.join(projectDir, "CurveRisk.slnx"))) process.exit(0);

const SOURCE = /\.(cs|csproj|props|targets|slnx|jsonl|editorconfig)$|(^|\/)global\.json$|Prompts\/.*\.md$|\/golden\/|\.verified\./i;

const git = args => spawnSync("git", args, { cwd: projectDir, encoding: "utf8" });
const status = git(["status", "--porcelain", "-uall"]);
if (status.status !== 0) process.exit(0);

const changed = status.stdout
  .split("\n")
  .map(line => line.slice(3).trim())
  .map(entry => (entry.includes(" -> ") ? entry.split(" -> ").pop() : entry)) // renames: verify the new path
  .map(file => file.replace(/^"|"$/g, ""))
  .filter(file => file && SOURCE.test(file));

// HEAD is part of the fingerprint so that "edit, commit, stop" is still verified: a clean tree after
// a commit is new, unverified state, not an absence of changes.
const head = git(["rev-parse", "--verify", "-q", "HEAD"]).stdout.trim() || "no-commits";

const fingerprint = createHash("sha256").update(`${head}\n`);
for (const file of changed.sort()) {
  const absolute = path.join(projectDir, file);
  fingerprint.update(`${file}:${existsSync(absolute) ? statSync(absolute).mtimeMs : "deleted"}\n`);
}
const digest = fingerprint.digest("hex");

const stampPath = path.join(projectDir, ".claude", ".verify-stamp");
if (existsSync(stampPath) && readFileSync(stampPath, "utf8") === digest) process.exit(0);

function run(label, args) {
  const result = spawnSync("dotnet", args, { cwd: projectDir, encoding: "utf8", timeout: 10 * 60_000 });
  if (result.status !== 0) {
    const output = `${result.stdout ?? ""}\n${result.stderr ?? ""}`.trim();
    console.error(`${label} failed. Fix it before finishing, or tell the user exactly what is broken and why.\n\n${output.slice(-4000)}`);
    process.exit(2);
  }
}

run("Build", ["build", "CurveRisk.slnx", "-nologo", "-v", "q"]);
run("Tests", ["test", "--no-build"]);
writeFileSync(stampPath, digest);
