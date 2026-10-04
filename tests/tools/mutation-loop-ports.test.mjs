// Tests for the mutation loop's git and ledger ports, against a real throwaway repository.
// Keep and revert are the loop's only writes to the working tree, so they are tested for real.

import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import os from "node:os";
import path from "node:path";
import { createPorts, parseAgentReply } from "../../tools/mutation-loop/ports.mjs";

const LEDGER = "docs/reports/mutation-loop.jsonl";

function repository(t) {
  const root = mkdtempSync(path.join(os.tmpdir(), "mutation-loop-"));
  t.after(() => rmSync(root, { recursive: true, force: true }));
  const git = (...args) => spawnSync("git", args, { cwd: root, encoding: "utf8" });
  const write = (file, text) => {
    mkdirSync(path.dirname(path.join(root, file)), { recursive: true });
    writeFileSync(path.join(root, file), text);
  };
  git("init", "-q");
  write("src/A.cs", "original");
  write("tests/ATests.cs", "original");
  git("add", "-A");
  git("-c", "user.name=test", "-c", "user.email=test@example.invalid", "commit", "-q", "-m", "initial");
  const read = file => readFileSync(path.join(root, file), "utf8");
  const ports = createPorts(root, { ledger: LEDGER });
  ports.begin();
  return { root, git, write, read, ports };
}

const files = ports => ports.changes().map(change => change.file).sort();

test("a fresh checkout is clean and has no changes", t => {
  const { ports } = repository(t);

  assert.equal(ports.isClean(), true);
  assert.deepEqual(ports.changes(), []);
});

test("changes are the modified and the new files, with the lines each lost", t => {
  const { ports, write } = repository(t);
  write("src/A.cs", "edited");
  write("tests/New Tests.cs", "new");
  write("tests/Tést.cs", "new");

  assert.deepEqual(ports.changes().sort((a, b) => a.file.localeCompare(b.file)), [
    { file: "src/A.cs", removedLines: 1 },
    { file: "tests/New Tests.cs", removedLines: 0 },
    { file: "tests/Tést.cs", removedLines: 0 },
  ]);
  assert.equal(ports.isClean(), false);
});

test("staging a change does not hide it, and a deleted file counts as lines removed", t => {
  const { root, ports, write, git } = repository(t);
  write("src/A.cs", "edited");
  write("tests/Staged.cs", "new");
  rmSync(path.join(root, "tests/ATests.cs"));
  git("add", "-A");

  assert.deepEqual(files(ports), ["src/A.cs", "tests/ATests.cs", "tests/Staged.cs"]);
  assert.equal(ports.changes().find(change => change.file === "tests/ATests.cs").removedLines, 1);
});

test("revert restores modified, staged and deleted files and removes new ones", t => {
  const { root, ports, write, read, git } = repository(t);
  write("src/A.cs", "edited");
  git("add", "-A");
  write("tests/NewTests.cs", "new");
  write("tests/Tést.cs", "new");
  rmSync(path.join(root, "tests/ATests.cs"));

  ports.revert();

  assert.equal(read("src/A.cs"), "original");
  assert.equal(read("tests/ATests.cs"), "original");
  assert.equal(existsSync(path.join(root, "tests/NewTests.cs")), false);
  assert.equal(existsSync(path.join(root, "tests/Tést.cs")), false);
  assert.equal(ports.isClean(), true);
});

test("keep stages the checked files only and drops whatever else appeared", t => {
  const { root, ports, write, read } = repository(t);
  write("tests/KeptTests.cs", "kept");
  write("src/A.cs", "written while the tests ran");

  ports.keep(["tests/KeptTests.cs"]);

  assert.equal(read("tests/KeptTests.cs"), "kept");
  assert.equal(read("src/A.cs"), "original");
  assert.deepEqual(ports.changes(), []);
  assert.equal(existsSync(path.join(root, "tests/KeptTests.cs")), true);
});

test("what was kept survives a later revert", t => {
  const { ports, write, read } = repository(t);
  write("tests/KeptTests.cs", "kept");
  ports.keep(["tests/KeptTests.cs"]);
  write("tests/KeptTests.cs", "kept, then spoiled");
  write("tests/RejectedTests.cs", "rejected");

  ports.revert();

  assert.equal(read("tests/KeptTests.cs"), "kept");
  assert.deepEqual(ports.changes(), []);
});

test("a commit made during an attempt stops the loop", t => {
  const { ports, write, git } = repository(t);
  write("src/A.cs", "edited");
  git("add", "-A");
  git("-c", "user.name=agent", "-c", "user.email=agent@example.invalid", "commit", "-q", "-m", "sneaked in");

  assert.throws(() => ports.changes(), /commit was made/);
});

test("the ledger is appended to, survives a revert, and an edit to it shows as a change", t => {
  const { ports, write, read } = repository(t);
  ports.record({ event: "start" });
  ports.record({ event: "stop" });

  ports.revert();

  const entries = read(LEDGER).trim().split("\n").map(line => JSON.parse(line));
  assert.deepEqual(entries.map(e => e.event), ["start", "stop"]);
  assert.match(entries[0].at, /^\d{4}-\d{2}-\d{2}T/);
  assert.deepEqual(ports.changes(), []);

  write(LEDGER, "rewritten by the agent");
  assert.deepEqual(files(ports), [LEDGER]);
});

test("an agent reply carries its cost and whether it succeeded", () => {
  const stdout = JSON.stringify({ result: "done", total_cost_usd: 0.42, is_error: false });

  assert.deepEqual(parseAgentReply({ ok: true, stdout, output: "" }, 3), { ok: true, costUsd: 0.42, text: "done" });
  assert.equal(parseAgentReply({ ok: true, stdout: JSON.stringify({ is_error: true, total_cost_usd: 1 }), output: "" }, 3).ok, false);
});

test("a reply that cannot be read is charged the whole budget it was given", () => {
  const reply = parseAgentReply({ ok: false, stdout: "", output: "claude: command not found" }, 3);

  assert.deepEqual(reply, { ok: false, costUsd: 3, text: "claude: command not found" });
  assert.equal(parseAgentReply({ ok: true, stdout: "{}", output: "" }, 3).costUsd, 3);
});
