# Code quality automation plan

Goal: every quality check that can run without a person does, at the earliest point it can, and nothing merges to `main` without passing all of them.

## Principles

1. **Deterministic tools decide; AI advises; people approve.** A linter or a coverage threshold gives the same answer every time, so it can block. An AI reviewer finds things tools cannot, but it varies run to run, so it comments and a person decides. The one exception is a measured AI check with a stable pass criterion (the Copilot evals).
2. **Shift left.** The same rule is cheapest to fix at edit time, dearer at commit, dearest in review. Each check runs at the earliest stage that can afford it.
3. **One gate, many triggers.** `node tools/check.mjs` is the definition of "passes". Hooks and CI call it; they do not re-implement it.
4. **Thresholds only go up.** A limit is lowered by a reviewed change with a reason, never to get a build through.
5. **Measure the checkers.** A test suite, a grader or an AI reviewer that has never been shown to catch a defect is unproven.

## Where each check runs

| Stage | Non-AI | AI |
|---|---|---|
| While editing | Analyzers in the IDE and on build; format on save | Skills (`clean-code`, `unit-testing`) loaded by the agent; `protect-paths` and `format-on-edit` hooks |
| Before the agent finishes | Full gate via the stop hook | `code-reviewer` subagent on its own diff |
| Commit | Format, size, lint of scripts and docs | |
| Push | Full gate | |
| Pull request | Full gate, secret scan, CodeQL, SonarQube Cloud gate, mutation score on changed code, architecture tests | `code-reviewer` and `security-reviewer` comment on the PR; Copilot evals when its behaviour can change |
| Merge to `main` | Required checks and CODEOWNERS approval | |
| Scheduled | Full mutation run, dependency updates, CodeQL, OpenSSF Scorecard | Weekly security sweep; review of Dependabot PRs; reviewer benchmark |

## Status

| Phase | State |
|---|---|
| 1. Switch on what exists | Repository public, clean history pushed, CI green on Linux, CodeQL and Scorecard running. **Waiting on the owner:** SonarQube Cloud account, a ruleset on `main`, and the one-line stop-hook change the agent is blocked from making |
| 2. Deterministic gaps | Done: branch coverage, architecture tests, ESLint, link check, suppression budget, CODEOWNERS, workflow lint, mutation testing (baseline 55.6%, reporting only). Not done: public-API tracking, Markdown style lint (the linter's own dependencies had known vulnerabilities, so it was dropped) |
| 3. Supply chain | Done: actions pinned to commit SHAs, OpenSSF Scorecard, npm and NuGet audits. Not done: SBOM, licence check, NuGet lock files, container scanning (no container yet) |
| 4. AI in the loop | Done: reviewer benchmark with a recorded baseline; self-review rule in `CLAUDE.md`; review, triage and security-sweep workflows written and switched off until an API key exists. All of it runs locally in Claude Code today. Not done: tests generated from surviving mutants; Dependabot PR review |
| 5. Visibility | Done: README badges. Not done: automatic threshold ratchet |

Decisions taken: AI checks never block a merge; AI runs locally until an API key is added; the mutation score is measured first and a break threshold set afterwards.

The original phase descriptions follow for reference.

## Phases

### Phase 1 — Switch on what exists (half a day, mostly settings)

1. Force-push the clean history; make the repository public.
2. Point the stop hook at `tools/check.mjs`.
3. Confirm the `ci` workflow is green on Linux; fix anything platform-specific.
4. Create the SonarQube Cloud project, add the token and variables, set its quality gate to the "Sonar way" defaults on new code.
5. Add a ruleset on `main`: pull request required, required checks `quality-gate`, `secrets-scan`, `codeql`, `sonarcloud`, no force pushes.
6. Add `ANTHROPIC_API_KEY` with a spend limit; enable AI review and live evals; record the first eval baseline.

Exit: a pull request that breaks any rule cannot be merged, and every workflow has run green at least once.

### Phase 2 — Close the gaps in deterministic checking (2 days)

| Addition | Tool | Enforces |
|---|---|---|
| Mutation testing | Stryker.NET | Tests actually detect changes to the code. Threshold on mutation score; changed files on PRs, full run weekly |
| Branch coverage | Extend `coverage-gate.mjs` | Coverage counts decisions, not just lines |
| Architecture tests | ArchUnitNET or NetArchTest, as ordinary tests | The invariants in `CLAUDE.md` become executable: only `Anthropic*` types reference the SDK; `Ai.Tools` does not reference `Copilot`; later, `Analytics` does no I/O |
| Public API tracking | `Microsoft.CodeAnalysis.PublicApiAnalyzers` | Any change to a public signature shows up in a diff and must be accepted deliberately |
| Script lint | ESLint for `.mjs` | The hooks and gate scripts are held to a standard too |
| Workflow lint | actionlint | Broken workflow syntax and unsafe expression use are caught before push |
| Docs lint | markdownlint, link check | Dead links and malformed Markdown |
| Suppression budget | A check in `check.mjs` | The count of `#pragma warning disable` and `[SuppressMessage]` cannot rise without changing a recorded number |
| Ownership | `CODEOWNERS` | Changes to hooks, `.editorconfig`, thresholds and eval datasets need a named human reviewer |

Exit: all of the above run inside `check.mjs` or as required PR checks.

### Phase 3 — Supply chain and security depth (1 day)

- Pin GitHub Actions to commit SHAs; Dependabot keeps them current.
- OpenSSF Scorecard workflow.
- SBOM generation (CycloneDX) on release; dependency licence check.
- NuGet package lock files with locked-mode restore in CI.
- Container image scan (Trivy) once a Dockerfile exists.

### Phase 4 — AI in the loop, measured (3 days)

1. **Reviewer benchmark.** A small set of code samples with planted defects (a null dereference, a SQL injection, an untested branch, a sync-over-async call) and some clean ones. Run `code-reviewer` and `security-reviewer` against it and score recall and false-positive rate. This turns "the AI review is good" into a number, and re-runs whenever a skill or brief changes.
2. **AI review on every PR**, comment-only, using the same skills as local work. A person resolves each finding.
3. **Agent self-review before finishing.** `CLAUDE.md` rule, backed by the stop hook: a change above a size threshold must have had a `code-reviewer` pass.
4. **Tests from evidence.** Feed surviving mutants and uncovered branches to the `test-author` subagent, which proposes tests in a PR. The mutation and coverage gates judge the result, not the agent.
5. **CI failure triage.** When the quality gate fails on a PR, an agent reads the log and opens a fix as a commit suggestion. It never merges.
6. **Dependency updates.** An agent reads each Dependabot PR's changelog and flags breaking changes.
7. **Weekly sweep.** Scheduled `security-reviewer` run over `main`, findings filed as issues.

Exit: the reviewer benchmark has a recorded baseline, and each AI step has a stated cost per run.

### Phase 5 — Visibility and upkeep (half a day)

- README badges: CI, coverage, Sonar quality gate, mutation score, Scorecard.
- A ratchet: when coverage or mutation score rises, the threshold in `quality.config.json` follows.
- Quarterly audit of skills, briefs and `CLAUDE.md` for rules that no longer match the code.

## What AI is not used for

- Deciding whether a build passes. Tools do that.
- Approving or merging its own changes.
- Editing thresholds, suppressions, eval cases or its own guard rails. The `protect-paths` hook and CODEOWNERS enforce this from both sides.

## Costs and limits

- Every AI step spends API credit: PR review, evals, test generation, triage, sweeps. Phase 4 should set a monthly cap and log cost per workflow.
- Mutation testing is slow. A full run belongs in a scheduled job; PRs test changed files only.
- SonarQube Cloud and CodeQL are free for public repositories and paid for private ones.
- More gates mean slower pushes. Pre-commit is already about 18 seconds; anything slower than a minute moves to CI.

## Decisions needed

1. **When the repository goes public.** It unlocks CodeQL, free SonarQube Cloud and free branch rules; Phase 1 depends on it.
2. **Whether to add the API key and what monthly spend limit to set.** Phase 4 and the dormant AI workflows depend on it.
3. **Whether any AI check should block a merge.** Recommended: no, except the Copilot evals, which have a deterministic pass criterion.
4. **Mutation score threshold.** Recommended: measure first, then set the threshold just under the measured score and ratchet up.
