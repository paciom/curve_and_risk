# AI reviewer benchmark

An AI reviewer that has never been shown to catch a defect is unproven. This benchmark gives the `code-reviewer` and `security-reviewer` subagents ten code samples with ten planted defects and two clean files, and scores what they report.

## What is measured

| Measure | Catches |
|---|---|
| Recall | A reviewer that says "looks good" to everything |
| Serious findings on clean files | A reviewer that flags everything |
| Unmatched serious findings | Noise elsewhere (each is listed, because some are real issues the benchmark did not plant) |

The planted defects are the kind static analysis struggles with: a refund larger than the payment, a 1-based page treated as 0-based, an ownership check present on delete but missing on read, an unsynchronised cache in a singleton, alongside classic injection, path traversal and secret-handling flaws. They are listed in [`expected.json`](expected.json), located by anchor text so line numbers cannot drift.

## Running it

In Claude Code, with no API key:

1. Ask the `code-reviewer` subagent to review `benchmarks/reviewer/cases/` and return findings as JSON (`file`, `line`, `severity`, `summary`). Tell it not to read anything else under `benchmarks/`.
2. Save the JSON and score it:

   ```bash
   node benchmarks/reviewer/score.mjs code findings.json
   ```

3. Repeat with `security-reviewer` and the `security` role.

Re-run whenever a review skill or subagent brief changes, and compare with [`baseline.json`](baseline.json).

## Limits

Ten samples and one run per reviewer is a smoke test, not a statistic: model output varies between runs, and a reviewer may have seen similar textbook defects before. The score shows the reviewers work on defects of this kind; it does not bound what they miss on real code. The reviewers are told to ignore the answer key, and nothing technically prevents them from reading it.
