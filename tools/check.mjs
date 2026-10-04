#!/usr/bin/env node
// The quality gate: one command, the same everywhere. The pre-push hook and CI both run this, so
// "passes locally" and "passes in CI" mean the same thing.
//
//   node tools/check.mjs            every gate
//   node tools/check.mjs --fast     the cheap ones only (used by pre-commit)
//
// Gates, in order of cost. The first failure stops the run and its output is shown.
//   format        dotnet format --verify-no-changes
//   size          file/function/parameter limits from the clean-code skill
//   lint          ESLint over the hooks and tool scripts
//   links         relative links in Markdown resolve
//   suppressions  count of silenced rules does not exceed the recorded budget
//   build         warnings as errors: .NET analyzers, SonarAnalyzer rules, banned APIs, code metrics,
//                 and the NuGet vulnerability audit all fail here
//   test          all tests (unit, wire-level, architecture), collecting coverage
//   coverage      line, branch and per-file thresholds from tools/quality.config.json
//   hooks         tests of the agent guard rails

import { spawnSync } from "node:child_process";
import { existsSync, readFileSync, readdirSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const config = JSON.parse(readFileSync(path.join(root, "tools", "quality.config.json"), "utf8"));
const fast = process.argv.includes("--fast");
const hookTests = readdirSync(path.join(root, "tests", "hooks")).filter(f => f.endsWith(".test.mjs")).map(f => `tests/hooks/${f}`);
const eslint = "node_modules/eslint/bin/eslint.js";

if (!existsSync(path.join(root, eslint))) {
  console.error("Lint tooling is not installed. Run: npm ci");
  process.exit(1);
}

const coverageArgs = ["--coverage", "--coverage-output-format", "cobertura", "--coverage-output", "coverage.cobertura.xml"];
const gates = [
  { name: "format", command: "dotnet", args: ["format", "CurveRisk.slnx", "--verify-no-changes"] },
  { name: "size", command: "node", args: [".claude/skills/clean-code/scripts/size-report.mjs", ...config.size.directories] },
  { name: "lint", command: "node", args: [eslint, "."] },
  { name: "links", command: "node", args: ["tools/check-links.mjs"] },
  { name: "suppressions", command: "node", args: ["tools/suppression-budget.mjs"] },
  { name: "build", command: "dotnet", args: ["build", "CurveRisk.slnx", "-nologo", "-v", "q"], full: true },
  { name: "test", command: "dotnet", args: ["test", "--no-build", ...coverageArgs], full: true },
  { name: "coverage", command: "node", args: ["tools/coverage-gate.mjs"], full: true },
  { name: "hooks", command: "node", args: ["--test", ...hookTests], full: true },
];

for (const gate of gates.filter(g => !fast || !g.full)) {
  const started = Date.now();
  const result = spawnSync(gate.command, gate.args, { cwd: root, encoding: "utf8" });
  const seconds = ((Date.now() - started) / 1000).toFixed(1);
  if (result.status !== 0) {
    const output = `${result.stdout ?? ""}\n${result.stderr ?? ""}`.trim();
    console.error(`FAIL  ${gate.name} (${seconds}s)\n\n${output.slice(-6000)}`);
    process.exit(1);
  }
  console.log(`ok    ${gate.name.padEnd(12)} (${seconds}s)`);
}
console.log(fast ? "Fast checks passed." : "All quality gates passed.");
