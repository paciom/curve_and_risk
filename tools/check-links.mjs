#!/usr/bin/env node
// Fails when a relative link in a tracked Markdown file points at something that does not exist.
// Documentation that links to a renamed file is wrong the moment the rename lands; this catches it then.

import { existsSync, readFileSync } from "node:fs";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const listed = spawnSync("git", ["ls-files", "--cached", "--others", "--exclude-standard", "*.md"], { cwd: root, encoding: "utf8" });
const files = listed.stdout.split("\n").filter(Boolean);

const broken = [];
for (const file of files) {
  if (!existsSync(path.join(root, file))) continue;
  const text = readFileSync(path.join(root, file), "utf8").replace(/```[\s\S]*?```/g, "");
  for (const match of text.matchAll(/\]\(([^)\s]+)\)/g)) {
    const target = match[1].split("#")[0];
    if (target === "" || /^[a-z][a-z0-9+.-]*:/i.test(target)) continue; // same-page anchor or absolute URL
    if (!existsSync(path.resolve(root, path.dirname(file), target))) broken.push(`${file}: ${match[1]}`);
  }
}

for (const entry of broken) console.log(entry);
console.log(`${files.length} Markdown file(s) checked, ${broken.length} broken link(s).`);
process.exit(broken.length > 0 ? 1 : 0);
