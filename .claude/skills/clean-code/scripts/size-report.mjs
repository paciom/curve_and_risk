#!/usr/bin/env node
// Reports files and functions that exceed the clean-code size defaults.
//
//   node size-report.mjs [dir ...] [--max-file 300] [--max-function 30] [--max-params 4]
//
// Exit 0 when nothing exceeds a limit, 1 otherwise. Supports C#, TypeScript and JavaScript.
// Function detection is a brace-matching heuristic, good enough to point at what to look at;
// it is not a parser, and a finding is a prompt to read the code, not a verdict.

import { readdirSync, readFileSync, statSync } from "node:fs";
import path from "node:path";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 ? Number(args[index + 1]) : fallback;
};
const maxFile = option("--max-file", 300);
const maxFunction = option("--max-function", 30);
const maxParams = option("--max-params", 4);
const roots = args.filter((arg, i) => !arg.startsWith("--") && !args[i - 1]?.startsWith("--"));
if (roots.length === 0) roots.push(".");

const EXTENSIONS = new Set([".cs", ".ts", ".tsx", ".js", ".mjs"]);
const SKIP = new Set(["bin", "obj", "node_modules", ".git", "dist", "Generated", "TestResults"]);
const KEYWORDS = /^(if|for|foreach|while|switch|catch|using|lock|else|do|try|finally|return|new|get|set|init|namespace|class|record|struct|interface|enum|function)$/;

function* walk(dir) {
  for (const entry of readdirSync(dir)) {
    if (SKIP.has(entry)) continue;
    const full = path.join(dir, entry);
    if (statSync(full).isDirectory()) yield* walk(full);
    else if (EXTENSIONS.has(path.extname(entry))) yield full;
  }
}

// Blank out strings and comments so braces inside them are not counted, keeping line numbers intact.
function strip(source) {
  return source.replace(
    /"""[\s\S]*?"""|\/\*[\s\S]*?\*\/|\/\/[^\n]*|@"(?:[^"]|"")*"|"(?:\\.|[^"\\\n])*"|'(?:\\.|[^'\\\n])*'|`(?:\\.|[^`\\])*`/g,
    match => match.replace(/[^\n]/g, " "));
}

// Commas inside generic arguments, tuples or defaults do not separate parameters: Func<A, B> is one.
function countParameters(list) {
  if (list.trim() === "") return 0;
  let depth = 0;
  let count = 1;
  for (const ch of list) {
    if (ch === "<" || ch === "[" || ch === "(") depth++;
    else if (ch === ">" || ch === "]" || ch === ")") depth--;
    else if (ch === "," && depth === 0) count++;
  }
  return count;
}

function functionsIn(source) {
  const text = strip(source);
  const found = [];
  // name(params) ... {   where what follows the parenthesis is optional modifiers/constraints/return type
  const signature = /([A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^<>()]*>)?\s*\(([^(){};]*)\)\s*(?::\s*[^{};=]+?)?(?:where\s[^{};]+?)?\s*\{/g;
  let match;
  while ((match = signature.exec(text)) !== null) {
    const name = match[1];
    if (KEYWORDS.test(name)) continue;
    // A type declared with a primary constructor - "class Foo(IBar bar) {" - looks like a function. It is not.
    if (/\b(class|record|struct|interface)\s+$/.test(text.slice(Math.max(0, match.index - 40), match.index))) continue;
    const open = match.index + match[0].length - 1;
    let depth = 0;
    let close = open;
    for (; close < text.length; close++) {
      if (text[close] === "{") depth++;
      else if (text[close] === "}" && --depth === 0) break;
    }
    const startLine = text.slice(0, match.index).split("\n").length;
    const body = text.slice(open + 1, close).split("\n").filter(line => line.trim().length > 0);
    const params = countParameters(match[2]);
    found.push({ name, startLine, lines: body.length, params });
  }
  return found;
}

function inspect(file) {
  const source = readFileSync(file, "utf8");
  const shown = path.relative(process.cwd(), file).split(path.sep).join("/");
  const found = [];

  const lineCount = source.split("\n").length;
  if (lineCount > maxFile) found.push(`${shown}: file has ${lineCount} lines (limit ${maxFile})`);

  for (const fn of functionsIn(source)) {
    if (fn.lines > maxFunction) found.push(`${shown}:${fn.startLine} ${fn.name}: ${fn.lines} lines (limit ${maxFunction})`);
    if (fn.params > maxParams) found.push(`${shown}:${fn.startLine} ${fn.name}: ${fn.params} parameters (limit ${maxParams})`);
  }
  return found;
}

const scanned = roots.flatMap(root => [...walk(root)]);
const findings = scanned.flatMap(inspect);
const files = scanned.length;

for (const finding of findings) console.log(finding);
console.log(`${files} file(s) scanned, ${findings.length} finding(s).`);
process.exit(findings.length > 0 ? 1 : 0);
