# Quality gates

Code quality here is enforced by tools, not by review comments or by asking an agent nicely. AI review and the agent skills sit on top of these gates; they do not replace them. A deterministic tool gives the same answer every time, cannot be talked out of it, and costs nothing to run.

## One command

```bash
node tools/check.mjs
```

| Gate | Tool | Fails when |
|---|---|---|
| format | `dotnet format --verify-no-changes` with [`.editorconfig`](../.editorconfig) | Any file is not formatted, or a style rule marked `error` is broken |
| size | [`size-report.mjs`](../.claude/skills/clean-code/scripts/size-report.mjs) | A file exceeds 300 lines, a function 30 lines, or a signature 4 parameters |
| lint | ESLint ([`eslint.config.mjs`](../eslint.config.mjs)) | The hook and tool scripts break a rule, including complexity, nesting and parameter limits |
| links | [`check-links.mjs`](../tools/check-links.mjs) | A relative link in any Markdown file points at nothing |
| suppressions | [`suppression-budget.mjs`](../tools/suppression-budget.mjs) | The number of silenced rules (`#pragma`, `SuppressMessage`, `NOSONAR`, `eslint-disable`, coverage exclusions) exceeds the recorded budget of 1 |
| build | Roslyn with `TreatWarningsAsErrors` | Any finding from the analyzers below |
| test | xUnit v3 on Microsoft.Testing.Platform, one run per test project | Any test fails, including the [architecture tests](../tests/CurveRisk.Ai.Tests/ArchitectureTests.cs) that turn the dependency rules in `CLAUDE.md` into assertions on the compiled assemblies |
| coverage | [`coverage-gate.mjs`](../tools/coverage-gate.mjs) with [`quality.config.json`](../tools/quality.config.json), merging the reports of every test project | Line coverage under 95%, branch coverage under 85%, or any single file under 85% |
| hooks | `node --test` | A guard-rail test fails |

## What runs inside the build

Configured in [`Directory.Build.props`](../Directory.Build.props); every finding is a compile error.

| Analyzer | What it enforces |
|---|---|
| .NET analyzers (`AnalysisLevel=latest-recommended`) | Reliability, performance, security and design rules (CA), plus IDE code-style rules at build time |
| [SonarAnalyzer.CSharp](https://www.nuget.org/packages/SonarAnalyzer.CSharp) | The SonarQube C# rule set, offline: bugs, code smells, cognitive and cyclomatic complexity, method length, nesting, hard-coded credentials, commented-out code |
| Code metrics ([`CodeMetricsConfig.txt`](../CodeMetricsConfig.txt)) | Cyclomatic complexity at most 10 per method, maintainability index, class coupling |
| Banned APIs ([`BannedSymbols.txt`](../BannedSymbols.txt)) | `DateTime.Now`, `Thread.Sleep`, `.Result`, `.Wait()`, `new Random()` and others; this is how "inject the clock" and "no sync-over-async" are enforced instead of hoped for |
| NuGet audit | Known-vulnerable packages, direct or transitive, fail the restore |
| Nullable reference types | Possible null dereferences |

Exceptions to rules are in `.editorconfig`, each with its reason on the same line. There are no `#pragma` suppressions in the source and one `[SuppressMessage]`, which carries a justification.

## Where it is enforced

| When | How | Scope |
|---|---|---|
| On every build | The compiler | All analyzers |
| Agent edits a C# file | `format-on-edit` hook | Formatting |
| Agent tries to end its turn | `verify-on-stop` hook | Build (all analyzers) and tests |
| `git commit` | [`.githooks/pre-commit`](../.githooks/pre-commit) | Format, size, lint, links, suppression budget |
| `git push` | [`.githooks/pre-push`](../.githooks/pre-push) | Full gate |
| Pull request and `main` | [`ci.yml`](../.github/workflows/ci.yml) | Full gate, secret scanning with gitleaks, workflow lint with actionlint |
| Pull request and `main` | [`codeql.yml`](../.github/workflows/codeql.yml) | Static security analysis for C# and JavaScript |
| `main` and weekly | [`scorecard.yml`](../.github/workflows/scorecard.yml) | OpenSSF Scorecard: supply-chain practices, scored externally |
| Pull request, weekly, on demand | [`mutation.yml`](../.github/workflows/mutation.yml) | Stryker.NET mutation testing; reports the score, does not yet block |
| Pull request and `main` | [`sonarcloud.yml`](../.github/workflows/sonarcloud.yml) | SonarQube Cloud quality gate (needs a token; see the file) |
| Weekly | [`dependabot.yml`](../.github/dependabot.yml) | Dependency and action updates |

Git hooks install themselves: [`Directory.Build.targets`](../Directory.Build.targets) sets `core.hooksPath` the first time anything is built.

Every third-party action is pinned to a commit SHA, with Dependabot keeping the pins current, so a compromised tag cannot change what the pipeline runs. [`CODEOWNERS`](../.github/CODEOWNERS) names a human reviewer for the files that define what passing means: thresholds, rule configuration, agent guard rails, eval datasets and the workflows themselves.

To make the CI checks binding, add a ruleset on `main` that requires a pull request, review from code owners, and the status checks `quality-gate`, `workflow-lint`, `secrets-scan` and `analyze`. That is a repository setting and cannot be done from a file.

## Mutation testing

Coverage says a line ran; the mutation score says a test would notice it being wrong. Stryker.NET makes small changes to the code and reruns the tests.

```bash
dotnet tool restore
```

```bash
dotnet stryker
```

First baseline, 2026-10-04: **55.6%** (360 killed of 604), against 96.8% line coverage. The gap is the finding: much of the code was executed by tests that did not assert on what it produced. Survivors cluster in user-facing message text, telemetry tag names and report formatting.

Current, with the pricing library and its closed-form tests added: **71.6%** (778 killed, 260 survived, 7 timed out, of 1,045 tested).

The break threshold is 0 for now, by decision: measure first, then agree a floor and ratchet it up. The `unit-testing` skill describes how to work through survivors.

Stryker needs `"test-runner": "mtp"` for this test project. With the default VSTest runner it reports every mutant as survived, which is a tool mismatch and not a score.

## AI checks

AI checks advise and never block a merge. The one exception by design is the Copilot eval suite, which has a deterministic pass criterion and is off until an API key is added.

| Check | Local, in Claude Code (no API key) | In CI (needs `ANTHROPIC_API_KEY` and an enabling variable) |
|---|---|---|
| Code review | `code-reviewer` subagent, `review-code` skill | [`claude-review.yml`](../.github/workflows/claude-review.yml), `ENABLE_CLAUDE_REVIEW` |
| Security review | `security-reviewer` subagent, `review-security` skill | [`claude-security-sweep.yml`](../.github/workflows/claude-security-sweep.yml), `ENABLE_CLAUDE_SWEEP` |
| Failure diagnosis | `debug-systematically` skill | [`claude-triage.yml`](../.github/workflows/claude-triage.yml), `ENABLE_CLAUDE_TRIAGE` |
| Copilot evals | Not available without a key | [`copilot-evals.yml`](../.github/workflows/copilot-evals.yml), `ENABLE_LIVE_EVALS` |

The reviewers themselves are measured: [`benchmarks/reviewer`](../benchmarks/reviewer/README.md) scores them against planted defects.

## Changing a threshold

Thresholds live in three files: `tools/quality.config.json` (coverage, size directories), `CodeMetricsConfig.txt` (complexity, coupling) and `.editorconfig` (rule severities and Sonar parameters). Lowering one is a reviewed change with a reason, the same as changing a test's expected value.
