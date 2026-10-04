#!/usr/bin/env node
// Coverage gate. Reads a Cobertura report, prints line and branch coverage per source file, and exits 1
// when the totals or any single file fall below the thresholds in tools/quality.config.json.
//
//   node tools/coverage-gate.mjs [path/to/coverage.cobertura.xml]
//
// A per-file floor matters as much as the total: a high average can hide one untested file. Branch
// coverage matters as much as line coverage: a line with an `if` can be "covered" with one outcome untested.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const config = JSON.parse(readFileSync(path.join(here, "quality.config.json"), "utf8")).coverage;
const reportPath = process.argv[2] ?? "TestResults/coverage.cobertura.xml";

function readReport(file) {
  try {
    return readFileSync(file, "utf8");
  } catch {
    console.error(`Coverage report not found at ${file}. Run the tests with --coverage first.`);
    process.exit(2);
  }
}

function attribute(tag, name) {
  return new RegExp(`\\b${name}="([^"]*)"`).exec(tag)?.[1];
}

function merge(previous = { hit: false, taken: 0, outcomes: 0 }, tag) {
  const condition = /\((\d+)\/(\d+)\)/.exec(attribute(tag, "condition-coverage") ?? "") ?? [0, 0, 0];
  return {
    hit: previous.hit || Number(attribute(tag, "hits")) > 0,
    taken: Math.max(previous.taken, Number(condition[1])),
    outcomes: Math.max(previous.outcomes, Number(condition[2])),
  };
}

// One entry per source line: whether it was hit, and how many of its branch outcomes were taken.
// The same line can appear under several <class> entries (lambdas, async state machines); keep the best.
function collect(xml) {
  const files = new Map();
  for (const cls of xml.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)) {
    const filename = attribute(cls[1], "filename")?.replace(/\\/g, "/") ?? "";
    const root = config.include.find(prefix => filename.includes(`/${prefix}`));
    if (!root || config.exclude.some(part => filename.includes(part))) continue;

    const name = filename.slice(filename.indexOf(`/${root}`) + 1);
    const lines = files.get(name) ?? new Map();
    for (const tag of cls[2].matchAll(/<line\b[^>]*>/g)) {
      const number = attribute(tag[0], "number");
      lines.set(number, merge(lines.get(number), tag[0]));
    }
    files.set(name, lines);
  }
  return files;
}

function summarise(lines) {
  const all = [...lines.values()];
  return {
    lines: all.length,
    hit: all.filter(line => line.hit).length,
    outcomes: all.reduce((sum, line) => sum + line.outcomes, 0),
    taken: all.reduce((sum, line) => sum + line.taken, 0),
  };
}

const percent = (part, whole) => (whole === 0 ? 100 : (100 * part) / whole);
const show = value => value.toFixed(1).padStart(5);

const files = collect(readReport(reportPath));
if (files.size === 0) {
  console.error("The coverage report contains no source files. Refusing to pass an empty report.");
  process.exit(2);
}

const total = { lines: 0, hit: 0, outcomes: 0, taken: 0 };
const failures = [];
console.log(`      ${"file".padEnd(56)}  line%  branch%`);
for (const [name, lines] of [...files].sort()) {
  const file = summarise(lines);
  for (const key of Object.keys(total)) total[key] += file[key];

  const linePercent = percent(file.hit, file.lines);
  const below = linePercent < config.minFileLinePercent;
  if (below) failures.push(`${name} is at ${linePercent.toFixed(1)}% lines (minimum per file ${config.minFileLinePercent}%)`);
  console.log(`${below ? "FAIL" : "  ok"}  ${name.padEnd(56)} ${show(linePercent)}   ${show(percent(file.taken, file.outcomes))}`);
}

const totalLine = percent(total.hit, total.lines);
const totalBranch = percent(total.taken, total.outcomes);
console.log(`      ${"TOTAL".padEnd(56)} ${show(totalLine)}   ${show(totalBranch)}`);
console.log(`      lines ${total.hit}/${total.lines}, branch outcomes ${total.taken}/${total.outcomes}`);

if (totalLine < config.minLinePercent) {
  failures.push(`total line coverage is ${totalLine.toFixed(1)}% (minimum ${config.minLinePercent}%)`);
}
if (totalBranch < config.minBranchPercent) {
  failures.push(`total branch coverage is ${totalBranch.toFixed(1)}% (minimum ${config.minBranchPercent}%)`);
}

if (failures.length > 0) {
  console.error(`\nCoverage gate failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}
console.log(`\nCoverage gate passed (lines >= ${config.minLinePercent}%, branches >= ${config.minBranchPercent}%, every file >= ${config.minFileLinePercent}% lines).`);
