#!/usr/bin/env node
// Scores an AI reviewer against the planted defects.
//
//   node benchmarks/reviewer/score.mjs <role> <findings.json>
//
// role is "code" or "security"; it selects which defects that reviewer is expected to find.
// findings.json is { "findings": [ { "file", "line", "severity", "summary" } ] }.
//
// Reports recall (planted defects found), false alarms on the clean files, and unmatched serious
// findings elsewhere. A reviewer that flags everything scores high recall and is caught by the
// false-alarm counts; a reviewer that says "looks good" is caught by recall.

import { readFileSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const [role, findingsPath] = process.argv.slice(2);
if (!["code", "security"].includes(role) || !findingsPath) {
  console.error("usage: node benchmarks/reviewer/score.mjs <code|security> <findings.json>");
  process.exit(2);
}

const expected = JSON.parse(readFileSync(path.join(here, "expected.json"), "utf8"));
const findings = JSON.parse(readFileSync(findingsPath, "utf8")).findings;
const SERIOUS = new Set(["blocker", "critical", "high", "major"]);

function windows(defect) {
  const lines = readFileSync(path.join(here, "cases", defect.file), "utf8").split("\n");
  return defect.anchors.map(anchor => {
    const line = lines.findIndex(text => text.includes(anchor)) + 1;
    if (line === 0) throw new Error(`Anchor not found in ${defect.file}: ${anchor}`);
    return [line - defect.before, line + defect.after];
  });
}

const baseName = file => String(file).split(/[\\/]/).pop();
const within = (finding, defect) =>
  baseName(finding.file) === defect.file && windows(defect).some(([low, high]) => finding.line >= low && finding.line <= high);

const owned = expected.defects.filter(defect => defect.owners.includes(role));
const found = owned.filter(defect => findings.some(finding => within(finding, defect)));
const missed = owned.filter(defect => !found.includes(defect));

const serious = findings.filter(finding => SERIOUS.has(String(finding.severity).toLowerCase()));
const onClean = serious.filter(finding => expected.cleanFiles.includes(baseName(finding.file)));
const unmatched = serious.filter(finding =>
  !expected.cleanFiles.includes(baseName(finding.file)) && !expected.defects.some(defect => within(finding, defect)));

const result = {
  role,
  recall: Number((found.length / owned.length).toFixed(2)),
  found: found.map(defect => defect.id),
  missed: missed.map(defect => defect.id),
  seriousFindingsOnCleanFiles: onClean.length,
  unmatchedSeriousFindings: unmatched.length,
  totalFindings: findings.length,
};
console.log(JSON.stringify(result, null, 2));
for (const finding of [...onClean, ...unmatched]) {
  console.log(`  unmatched: ${baseName(finding.file)}:${finding.line} [${finding.severity}] ${String(finding.summary).slice(0, 110)}`);
}
