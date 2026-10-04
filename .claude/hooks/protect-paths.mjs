#!/usr/bin/env node
// PreToolUse hook (Edit | Write | NotebookEdit).
//
// Blocks agent edits to files whose whole value is that the agent did not write them: reference
// numbers, eval cases, generated clients, accepted snapshots - and the guard rails themselves. An
// agent asked to "make the tests pass" will otherwise, sooner or later, do it by changing the
// expected answer or by loosening the check.
//
// A person who really means to change one of these sets CURVERISK_ALLOW_PROTECTED_EDITS=1 for that session.
// Exit 2 blocks the tool call and shows stderr to the agent.

import { readFileSync } from "node:fs";
import path from "node:path";

// Case-insensitive: the file systems this runs on (Windows, default macOS) are.
const PROTECTED = [
  { pattern: /(^|\/)tests\/(.*\/)?golden\//i, why: "golden reference values come from an independent library (see the numerical-validation skill)" },
  { pattern: /(^|\/)evals\/datasets\//i, why: "eval cases define correct behaviour; fix the agent, not the case" },
  { pattern: /(^|\/)generated\//i, why: "generated code is rebuilt from the OpenAPI document, not edited" },
  { pattern: /\.verified\.[^/]+$/i, why: "contract snapshots are accepted by a person after reviewing the diff" },
  { pattern: /^\.claude\/(hooks\/|settings\.json$)/i, why: "an agent does not edit its own guard rails; propose the change to the user" },
];

// Git Bash and MSYS tools write D:\x as /d/x. Without this, such a path looks like it is outside the
// project and would be waved through.
function normalise(filePath) {
  const msys = /^\/([a-zA-Z])\/(.*)$/.exec(filePath);
  return process.platform === "win32" && msys ? `${msys[1]}:/${msys[2]}` : filePath;
}

function check(filePath, projectDir) {
  if (!filePath || process.env.CURVERISK_ALLOW_PROTECTED_EDITS === "1") return null;
  const root = normalise(projectDir);
  const relative = path.relative(root, path.resolve(root, normalise(filePath))).split(path.sep).join("/");
  if (relative.startsWith("..") || path.isAbsolute(relative)) return null;
  const hit = PROTECTED.find(rule => rule.pattern.test(relative));
  return hit ? `Blocked: ${relative} is protected because ${hit.why}. If the file itself is wrong, stop and tell the user what you found.` : null;
}

let input = {};
try {
  input = JSON.parse(readFileSync(0, "utf8"));
} catch {
  process.exit(0); // Never block on a malformed payload; this hook guards files, it must not break the harness.
}

const target = input.tool_input?.file_path ?? input.tool_input?.notebook_path;
const message = check(target, process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd());
if (message) {
  console.error(message);
  process.exit(2);
}
