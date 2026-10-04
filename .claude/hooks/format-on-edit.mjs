#!/usr/bin/env node
// PostToolUse hook (Edit | Write).
//
// Normalises whitespace in the C# file that was just edited, so formatting never shows up in a diff
// or a review. Uses the folder mode of dotnet format: it does not load the MSBuild workspace, so it
// takes about a second. Code-style rules are enforced separately, at build time.

import { readFileSync, existsSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";

let input = {};
try {
  input = JSON.parse(readFileSync(0, "utf8"));
} catch {
  process.exit(0);
}

const projectDir = process.env.CLAUDE_PROJECT_DIR ?? input.cwd ?? process.cwd();
const filePath = input.tool_input?.file_path;
if (!filePath || !filePath.endsWith(".cs")) process.exit(0);

const absolute = path.resolve(projectDir, filePath);
const relative = path.relative(projectDir, absolute);
if (relative.startsWith("..") || !existsSync(absolute)) process.exit(0);

const result = spawnSync("dotnet", ["format", "whitespace", "--folder", ".", "--include", relative], {
  cwd: projectDir,
  encoding: "utf8",
  timeout: 60_000,
});

if (result.status !== 0) {
  // Exit 2 on PostToolUse feeds stderr back to the agent without undoing the edit.
  console.error(`dotnet format failed for ${relative}:\n${(result.stderr || result.stdout || "").trim().slice(-1500)}`);
  process.exit(2);
}
