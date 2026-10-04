#!/usr/bin/env node
// Suppression budget. Counts the ways a rule can be silenced in source and fails when the count
// exceeds the number recorded in tools/quality.config.json.
//
// Without this, every other gate has a back door: "#pragma warning disable" makes any analyzer pass.
// With it, silencing a rule means changing a recorded number in a file that needs a human reviewer.

import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const config = JSON.parse(readFileSync(path.join(root, "tools", "quality.config.json"), "utf8")).suppressions;

const PATTERNS = [
  /#pragma\s+warning\s+disable/,
  /\bSuppressMessage\s*\(/,
  /\/\/\s*NOSONAR/i,
  /\bExcludeFromCodeCoverage\b/,
  /#nullable\s+disable/,
  /eslint-disable/,
];
const SKIP = new Set(["bin", "obj", "node_modules", ".git", "TestResults", "StrykerOutput"]);
const EXTENSIONS = new Set([".cs", ".mjs", ".js", ".ts"]);

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (SKIP.has(entry)) continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) yield* walk(full);
    else if (EXTENSIONS.has(path.extname(entry))) yield full;
  }
}

const self = fileURLToPath(import.meta.url);
const found = [];
for (const directory of config.directories) {
  for (const file of walk(path.join(root, directory))) {
    if (file === self) continue; // this file names the patterns it looks for
    readFileSync(file, "utf8").split("\n").forEach((line, index) => {
      if (PATTERNS.some(pattern => pattern.test(line))) {
        found.push(`${path.relative(root, file).split(path.sep).join("/")}:${index + 1}  ${line.trim().slice(0, 100)}`);
      }
    });
  }
}

for (const entry of found) console.log(entry);
console.log(`${found.length} suppression(s); budget is ${config.max}.`);
if (found.length > config.max) {
  console.error("Over budget. Fix the code instead of silencing the rule, or have a person raise the budget with a reason.");
  process.exit(1);
}
