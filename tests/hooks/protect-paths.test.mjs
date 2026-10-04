// Tests for the agent guard rail. Run with: node --test "tests/hooks/*.test.mjs"
// The hook is exercised as the harness runs it: a JSON payload on stdin, the exit code as the verdict.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const hook = path.join(root, ".claude", "hooks", "protect-paths.mjs");

function run(filePath, env = {}) {
  const result = spawnSync("node", [hook], {
    input: JSON.stringify({ tool_name: "Edit", tool_input: { file_path: filePath } }),
    encoding: "utf8",
    env: { ...process.env, CLAUDE_PROJECT_DIR: root, CURVERISK_ALLOW_PROTECTED_EDITS: "", ...env },
  });
  return { blocked: result.status === 2, status: result.status, stderr: result.stderr };
}

const blocked = [
  "evals/datasets/copilot.jsonl",
  "Evals/Datasets/copilot.jsonl",
  path.join(root, "evals", "datasets", "copilot.jsonl"),
  "tests/CurveRisk.Analytics.Tests/golden/ois.json",
  "tests/golden/ois.json",
  "tests/X/Golden/ois.json",
  "src/CurveRisk.Api/Generated/Client.cs",
  "web/src/generated/api.ts",
  "tests/X/OpenApi.v1.verified.json",
  "tests/X/OpenApi.v1.VERIFIED.TXT",
  "tests/X/schema.verified.xml",
  ".claude/hooks/protect-paths.mjs",
  ".claude/settings.json",
];

const allowed = [
  "src/CurveRisk.Copilot/CopilotAgent.cs",
  "tests/CurveRisk.Ai.Tests/NumericGroundingTests.cs",
  "evals/CurveRisk.Evals/Graders.cs",
  ".claude/skills/quant-review/SKILL.md",
  ".claude/settings.local.json",
  "docs/golden-rules.md",
];

for (const file of blocked) {
  test(`blocks ${file}`, () => {
    const result = run(file);
    assert.equal(result.status, 2, result.stderr);
    assert.match(result.stderr, /protected because/);
  });
}

for (const file of allowed) {
  test(`allows ${file}`, () => assert.equal(run(file).status, 0));
}

test("a path outside the project is not this hook's business", () => {
  assert.equal(run(path.resolve(root, "..", "elsewhere", "evals", "datasets", "x.jsonl")).status, 0);
});

test("a person can override for a session", () => {
  assert.equal(run("evals/datasets/copilot.jsonl", { CURVERISK_ALLOW_PROTECTED_EDITS: "1" }).status, 0);
});

test("MSYS-style drive paths are recognised on Windows", { skip: process.platform !== "win32" }, () => {
  const msys = "/" + root[0].toLowerCase() + root.slice(2).replace(/\\/g, "/") + "/evals/datasets/copilot.jsonl";
  assert.equal(run(msys).status, 2);
});
