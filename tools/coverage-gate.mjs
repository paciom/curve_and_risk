#!/usr/bin/env node
// Coverage gate. Reads every Cobertura report in a directory (one per test project), merges them,
// prints line and branch coverage per source file, and exits 1 when the totals or any single file
// fall below the thresholds in tools/quality.config.json.
//
//   node tools/coverage-gate.mjs [directory containing *.cobertura.xml]
//
// A per-file floor matters as much as the total: a high average can hide one untested file. Branch
// coverage matters as much as line coverage: a line with an `if` can be "covered" with one outcome untested.

import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const config = JSON.parse(readFileSync(path.join(here, "quality.config.json"), "utf8")).coverage;
const reportDirectory = process.argv[2] ?? "TestResults";

function readReports(directory) {
  let names = [];
  try {
    names = readdirSync(directory).filter(name => name.endsWith(".cobertura.xml"));
  } catch {
    // Reported below, with the same message as an empty directory.
  }
  if (names.length === 0) {
    console.error(`No coverage reports found in ${directory}. Run the tests with --coverage first.`);
    process.exit(2);
  }
  return names.map(name => readFileSync(path.join(directory, name), "utf8"));
}

function attribute(tag, name) {
  return new RegExp(`\\b${name}="([^"]*)"`).exec(tag)?.[1];
}

// A line can appear in several reports and under several <class> entries (lambdas, async state
// machines). It is covered if any of them hit it, and its branch count is the best any of them saw.
function merge(previous = { hit: false, taken: 0, outcomes: 0 }, tag) {
  const condition = /\((\d+)\/(\d+)\)/.exec(attribute(tag, "condition-coverage") ?? "") ?? [0, 0, 0];
  return {
    hit: previous.hit || Number(attribute(tag, "hits")) > 0,
    taken: Math.max(previous.taken, Number(condition[1])),
    outcomes: Math.max(previous.outcomes, Number(condition[2])),
  };
}

function sourceName(filename) {
  const normalised = filename.replaceAll("\\", "/");
  const root = config.include.find(prefix => normalised.includes(`/${prefix}`));
  const excluded = config.exclude.some(part => normalised.includes(part));
  return root && !excluded ? normalised.slice(normalised.indexOf(`/${root}`) + 1) : null;
}

function collect(reports) {
  const files = new Map();
  for (const cls of reports.flatMap(xml => [...xml.matchAll(/<class\b([^>]*)>([\s\S]*?)<\/class>/g)])) {
    const name = sourceName(attribute(cls[1], "filename") ?? "");
    if (!name) continue;

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

const files = collect(readReports(reportDirectory));
if (files.size === 0) {
  console.error("The coverage reports contain no source files. Refusing to pass an empty report.");
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
