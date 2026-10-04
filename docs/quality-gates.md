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
| build | Roslyn with `TreatWarningsAsErrors` | Any finding from the analyzers below |
| test | xUnit v3 on Microsoft.Testing.Platform | Any test fails |
| coverage | [`coverage-gate.mjs`](../tools/coverage-gate.mjs) with [`quality.config.json`](../tools/quality.config.json) | Total line coverage is under 95%, or any single file is under 85% |
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
| `git commit` | [`.githooks/pre-commit`](../.githooks/pre-commit) | Format and size |
| `git push` | [`.githooks/pre-push`](../.githooks/pre-push) | Full gate |
| Pull request and `main` | [`ci.yml`](../.github/workflows/ci.yml) | Full gate, plus secret scanning with gitleaks |
| Pull request and `main` | [`codeql.yml`](../.github/workflows/codeql.yml) | Static security analysis for C# and JavaScript (public repositories only, unless GitHub Advanced Security is enabled) |
| Pull request and `main` | [`sonarcloud.yml`](../.github/workflows/sonarcloud.yml) | SonarQube Cloud quality gate (needs a token; see the file) |
| Weekly | [`dependabot.yml`](../.github/dependabot.yml) | Dependency and action updates |

Git hooks install themselves: [`Directory.Build.targets`](../Directory.Build.targets) sets `core.hooksPath` the first time anything is built.

To make the CI checks binding, mark `quality-gate`, `secrets-scan`, `codeql` and `sonarcloud` as required status checks in the branch protection rule for `main`. That is a repository setting and cannot be done from a file.

## Changing a threshold

Thresholds live in three files: `tools/quality.config.json` (coverage, size directories), `CodeMetricsConfig.txt` (complexity, coupling) and `.editorconfig` (rule severities and Sonar parameters). Lowering one is a reviewed change with a reason, the same as changing a test's expected value.
