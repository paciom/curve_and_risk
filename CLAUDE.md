# Curve & Risk

Interest-rate curve calibration and risk platform. .NET 10, ASP.NET Core, PostgreSQL, Aspire. See `PLAN.md` for scope and phases, `docs/adr/` for decisions, `docs/ai-workflow.md` for how agents are used here.

Only the AI layer exists so far. `FixtureRiskEngine` is a placeholder for the real analytics library.

## Commands

```bash
node tools/check.mjs                 # THE quality gate: format, size, build+analyzers, tests, coverage, hook tests
dotnet build CurveRisk.slnx          # every analyzer finding is a build error
dotnet test                          # Microsoft.Testing.Platform runner; no credentials or network needed
dotnet format CurveRisk.slnx         # fix formatting
dotnet run --project evals/CurveRisk.Evals -- --trials 3    # LIVE model calls, costs money: ask first
```

The MCP server in `.mcp.json` runs the prebuilt Release binary: build it once with `dotnet build src/CurveRisk.Mcp -c Release`, and again after changing a tool. Running Release keeps a live server from locking the Debug binaries that `dotnet build` and `dotnet test` write, and starting the DLL directly avoids the `dotnet run` start-up delay that makes MCP clients time out. It offers read tools only unless started with `--allow-writes`.

## Layout

| Path | What it is |
|---|---|
| `src/CurveRisk.Ai.Tools` | `IRiskEngine` port and `ToolCatalog`: the one definition of every AI tool |
| `src/CurveRisk.Mcp` | MCP stdio server exposing the catalog to external agents |
| `src/CurveRisk.Copilot` | In-product agent: `CopilotAgent` (loop), `AskSession` (per-question state), `ToolExecutor` (approval gate), `NumericGrounding`, cost, telemetry |
| `evals/` | Dataset, deterministic graders, live runner |
| `tests/CurveRisk.Ai.Tests` | Unit and wire-level tests, including tests of the graders themselves |
| `tools/` | `check.mjs` (the quality gate), `coverage-gate.mjs`, thresholds in `quality.config.json` |

## Invariants

These are the product's guarantees. A change that weakens one needs an ADR, not a quiet edit.

1. **The model never originates a number.** Figures reach users only via `IRiskEngine`. `NumericGrounding` enforces it on every answer; do not loosen its matching to make an answer pass.
2. **Writes need a human.** A tool that changes state is registered with `RequiresApproval: true` and goes through `IApprovalGate`. The prompt is not a security control.
3. **One tool definition.** Add tools in `RiskTools` + `ToolCatalog` only. Both the MCP server and the Copilot derive from it.
4. **The cached prefix is stable.** No timestamps, ids or per-request data in the system prompt or tool descriptions; tool order is fixed.
5. **Only the `Anthropic*` adapter files know the SDK.** Everything else depends on `IModelClient`, which is what makes the agent testable offline.

## Quality gates

Enforced by tools; see `docs/quality-gates.md`. Run `node tools/check.mjs` before reporting work as done.

- The build runs the .NET analyzers, SonarAnalyzer (SonarQube rules), code metrics (cyclomatic complexity 10) and a banned-API list. A finding is a compile error.
- Fix the code, not the rule. Do not add `#pragma warning disable`, `[SuppressMessage]`, or lower a severity or threshold to get a build through. If a rule is wrong for a case, say so and let the user decide; an agreed exception goes in `.editorconfig` with its reason.
- Coverage must stay at or above 95% in total and 85% per file (`tools/quality.config.json`). New code comes with its tests.
- `DateTime.Now`, `Thread.Sleep`, `.Result` and `.Wait()` are banned: inject `TimeProvider`, and await.

## Working rules

- Files under `evals/datasets/`, `tests/**/golden/`, `**/Generated/` and `*.verified.*` are blocked by a hook. If one of them looks wrong, stop and say so; do not work around the block.
- A failing eval or test is information about the code. Fix the code, or report that the expectation is wrong and why.
- Quant code states its conventions in the signature or the type: day count, compounding, units (percent vs fraction vs bp), sign. `ScenarioShock` and `RiskReport` show the style.
- Async all the way with `CancellationToken`; `ConfigureAwait(false)` in library code.
- New behaviour comes with a test that fails without it. For graders and guards, also a test showing they can fail.
- `CurveRisk.Analytics` and `CurveRisk.Contracts` (when they exist) target `netstandard2.0`: no `Span`-only APIs, no default interface members, no `init` without the polyfill.
- When you touch the Claude API surface, read the `claude-api` skill first; SDK type names are not guessable.

## Skills and subagents

General software engineering skills in `.claude/skills/`. Use them on every change, in any part of the codebase:

- `clean-code` before writing or modifying code. Run its size check on what you touched: `node .claude/skills/clean-code/scripts/size-report.mjs src evals tests` must report no findings.
- `unit-testing` when writing tests or raising coverage.
- `review-code` before reporting a change as done; `review-security` when the change touches input handling, auth, secrets, files or the network.
- `refactor-safely` for structural changes; `debug-systematically` when something fails.

Project skills, for work specific to this codebase: `add-copilot-tool`, `run-copilot-evals`, `add-endpoint`, `ef-migration`, `add-instrument`, `numerical-validation`, `quant-review`.

Subagents in `.claude/agents/` review with fresh context. General: `code-reviewer`, `security-reviewer`, `test-author`, `api-contract-guardian`. Project: `guardrail-reviewer`, `quant-reviewer`. Give a reviewer the diff and the requirement, not your conclusion.
