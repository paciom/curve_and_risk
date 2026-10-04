// Reads a Stryker JSON report into the facts the mutation loop decides on. Pure: no I/O.
//
// A mutant is identified by where it is and what it does, not by Stryker's id, which changes from
// run to run. The loop never lets the source under test change, so the position is stable.

const DETECTED = new Set(["Killed", "Timeout"]);
const UNDETECTED = new Set(["Survived", "NoCoverage"]);

export const isDetected = mutant => DETECTED.has(mutant.status);

const relativeTo = (root, file) => {
  const normalised = file.replaceAll("\\", "/");
  const prefix = `${root.replaceAll("\\", "/").replace(/\/$/, "")}/`;
  return normalised.toLowerCase().startsWith(prefix.toLowerCase()) ? normalised.slice(prefix.length) : normalised;
};

function toMutant(file, sourceLines, raw) {
  const { line, column } = raw.location.start;
  const text = sourceLines[line - 1] ?? "";
  const sameLine = raw.location.end.line === line;
  return {
    key: `${file}:${line}:${column}:${raw.mutatorName}:${raw.replacement}`,
    file,
    line,
    column,
    mutator: raw.mutatorName,
    replacement: raw.replacement,
    original: sameLine ? text.slice(column - 1, raw.location.end.column - 1) : text.slice(column - 1).trim(),
    before: text.slice(0, column - 1),
    status: raw.status,
  };
}

/** Every mutant in the report, with paths relative to the repository root and forward slashes. */
export function readMutants(report) {
  return Object.entries(report.files).flatMap(([absolute, entry]) => {
    const file = relativeTo(report.projectRoot ?? "", absolute);
    const sourceLines = (entry.source ?? "").split(/\r?\n/);
    return entry.mutants.map(raw => toMutant(file, sourceLines, raw));
  });
}

/** Stryker's score: detected over detected plus undetected. Ignored and non-compiling mutants do not count. */
export function score(mutants) {
  const detected = mutants.filter(isDetected).length;
  const undetected = mutants.filter(m => UNDETECTED.has(m.status)).length;
  return { detected, undetected };
}

export const percent = ({ detected, undetected }) =>
  detected + undetected === 0 ? 100 : (100 * detected) / (detected + undetected);

/** The rule that declares a mutant unobservable by any test, or undefined. Rules come from configuration, never from the agent. */
export const unobservableRule = (mutant, rules) => rules.find(rule => mutant.before.endsWith(rule.precededBy));

/** Undetected mutants a test could in principle catch, grouped by file, the file with most survivors first. */
export function targetsByFile(mutants, rules) {
  const groups = new Map();
  for (const mutant of mutants) {
    if (!UNDETECTED.has(mutant.status) || unobservableRule(mutant, rules)) continue;
    groups.set(mutant.file, [...(groups.get(mutant.file) ?? []), mutant]);
  }
  return [...groups.entries()]
    .map(([file, targets]) => ({ file, targets }))
    .sort((a, b) => b.targets.length - a.targets.length || a.file.localeCompare(b.file));
}

/** Splits the targets into those a later measurement shows detected and those it does not. */
export function splitByOutcome(targets, measuredMutants) {
  const detectedKeys = new Set(measuredMutants.filter(isDetected).map(m => m.key));
  return {
    killed: targets.filter(t => detectedKeys.has(t.key)),
    remaining: targets.filter(t => !detectedKeys.has(t.key)),
  };
}
