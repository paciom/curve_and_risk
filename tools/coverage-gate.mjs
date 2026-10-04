#!/usr/bin/env node
// Coverage gate. Reads a Cobertura report, prints line coverage per source file, and exits 1 when the
// total or any single file is below the thresholds in tools/quality.config.json.
//
//   node tools/coverage-gate.mjs [path/to/coverage.cobertura.xml]
//
// A per-file floor matters as much as the total: a high average can hide one untested file.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const config = JSON.parse(readFileSync(path.join(here, "quality.config.json"), "utf8")).coverage;
const reportPath = process.argv[2] ?? "TestResults/coverage.cobertura.xml";

let xml;
try {
  xml = readFileSync(reportPath, "utf8");
} catch {
  console.error(`Coverage report not found at ${reportPath}. Run the tests with --coverage first.`);
  process.exit(2);
}

// Map each included source file to its lines; a line counts as covered if any class entry hit it.
const files = new Map();
for (const cls of xml.matchAll(/<class\b[^>]*\bfilename="([^"]+)"[^>]*>([\s\S]*?)<\/class>/g)) {
  const normalised = cls[1].replace(/\\/g, "/");
  const root = config.include.find(prefix => normalised.includes(`/${prefix}`));
  if (!root || config.exclude.some(part => normalised.includes(part))) continue;

  const name = normalised.slice(normalised.indexOf(`/${root}`) + 1);
  const lines = files.get(name) ?? new Map();
  for (const line of cls[2].matchAll(/<line\b[^>]*\bnumber="(\d+)"[^>]*\bhits="(\d+)"/g)) {
    lines.set(line[1], (lines.get(line[1]) ?? false) || Number(line[2]) > 0);
  }
  files.set(name, lines);
}

if (files.size === 0) {
  console.error("The coverage report contains no source files. Refusing to pass an empty report.");
  process.exit(2);
}

let total = 0;
let covered = 0;
const failures = [];
for (const [name, lines] of [...files].sort()) {
  const hit = [...lines.values()].filter(Boolean).length;
  const percent = (100 * hit) / lines.size;
  total += lines.size;
  covered += hit;
  const below = percent < config.minFileLinePercent;
  if (below) failures.push(`${name} is at ${percent.toFixed(1)}% (minimum per file ${config.minFileLinePercent}%)`);
  console.log(`${below ? "FAIL" : "  ok"}  ${name.padEnd(56)} ${String(hit).padStart(4)}/${String(lines.size).padEnd(4)} ${percent.toFixed(1).padStart(5)}%`);
}

const totalPercent = (100 * covered) / total;
console.log(`      ${"TOTAL".padEnd(56)} ${String(covered).padStart(4)}/${String(total).padEnd(4)} ${totalPercent.toFixed(1).padStart(5)}%`);
if (totalPercent < config.minLinePercent) {
  failures.push(`total line coverage is ${totalPercent.toFixed(1)}% (minimum ${config.minLinePercent}%)`);
}

if (failures.length > 0) {
  console.error(`\nCoverage gate failed:\n- ${failures.join("\n- ")}`);
  process.exit(1);
}
console.log(`\nCoverage gate passed (total >= ${config.minLinePercent}%, every file >= ${config.minFileLinePercent}%).`);
