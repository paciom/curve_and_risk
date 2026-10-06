// Tests for the mutation loop's command line, run as a real process in dry-run mode: nothing is called.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..", "..");
const EMPTY_REPORT = JSON.stringify({ files: {} });

function dryRun(report, cwd = root) {
  return spawnSync(process.execPath, [path.join(root, "tools", "mutation-loop.mjs"), "--dry-run", "--report", report], { cwd, encoding: "utf8" });
}

function reportIn(t, parent) {
  const directory = mkdtempSync(path.join(parent, "mutation-loop-cli-"));
  t.after(() => rmSync(directory, { recursive: true, force: true }));
  const file = path.join(directory, "mutation-report.json");
  writeFileSync(file, EMPTY_REPORT);
  return file;
}

test("a report inside the checkout is read, by absolute or relative path", t => {
  const file = reportIn(t, path.join(root, "tests", "tools"));

  for (const given of [file, path.relative(root, file)]) {
    const result = dryRun(given);
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /Would stop now/);
  }
});

test("a report outside the checkout is refused without being read", t => {
  const outside = reportIn(t, os.tmpdir());

  for (const given of [outside, path.relative(root, outside)]) {
    const result = dryRun(given);
    assert.equal(result.status, 1);
    assert.match(result.stderr, /--report must be a file inside this checkout/);
    assert.equal(result.stdout, "");
  }
});
