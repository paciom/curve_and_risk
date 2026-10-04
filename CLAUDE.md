# Curve & Risk

Interest-rate curve calibration and risk platform. .NET 10, ASP.NET Core, PostgreSQL, Aspire. See `PLAN.md` for scope and phases, `docs/adr/` for decisions, `docs/ai-workflow.md` for how agents are used here.

Built so far: the pricing library, the engine that exposes it, the REST API with persistence and a small web page, the AI layer (tools, MCP server, Copilot, evals) and the quality pipeline. Not yet built: database migrations, market-data imports, Aspire orchestration, deployment.

## Commands

```bash
node tools/check.mjs                 # THE quality gate: format, size, build+analyzers, tests, coverage, hook tests
dotnet run --project src/CurveRisk.Api   # API and web page on SQLite; open the URL it prints
dotnet build CurveRisk.slnx          # every analyzer finding is a build error
dotnet test                          # Microsoft.Testing.Platform runner; no credentials or network needed
dotnet format CurveRisk.slnx         # fix formatting
dotnet run --project evals/CurveRisk.Evals -- --trials 3    # LIVE model calls, costs money: ask first
node tools/mutation-loop.mjs --dry-run --report <report.json>   # plan of the mutation-kill loop; without --dry-run it is LIVE: ask first
```

The MCP server in `.mcp.json` runs the prebuilt Release binary: build it once with `dotnet build src/CurveRisk.Mcp -c Release`, and again after changing a tool. Running Release keeps a live server from locking the Debug binaries that `dotnet build` and `dotnet test` write, and starting the DLL directly avoids the `dotnet run` start-up delay that makes MCP clients time out. It offers read tools only unless started with `--allow-writes`.

## Layout

| Path | What it is |
|---|---|
| `src/CurveRisk.Analytics` | Pricing library, `netstandard2.0`, pure: conventions, curves, calibration, instruments, risk |
| `src/CurveRisk.Engine` | `AnalyticsRiskEngine`: adapts the library to the `IRiskEngine` port; percent and bp at this boundary, fractions inside |
| `src/CurveRisk.Contracts` | API request and response records, `netstandard2.0`, no dependencies |
| `src/CurveRisk.Api` | ASP.NET Core API under `/api/v1`: endpoints, services, EF Core persistence, the web page in `wwwroot` |
| `src/CurveRisk.Ai.Tools` | `IRiskEngine` port and `ToolCatalog`: the one definition of every AI tool |
| `src/CurveRisk.Mcp` | MCP stdio server exposing the catalog to external agents |
| `src/CurveRisk.Copilot` | In-product agent: `CopilotAgent` (loop), `AskSession` (per-question state), `ToolExecutor` (approval gate), `NumericGrounding`, cost, telemetry |
| `evals/` | Dataset, deterministic graders, live runner |
| `tests/CurveRisk.Ai.Tests` | Unit, wire-level and architecture tests, including tests of the graders themselves |
| `tests/CurveRisk.Analytics.Tests` | Closed-form and property tests of the pricing library |
| `tests/CurveRisk.Api.Tests` | The real API in process on SQLite (PostgreSQL in CI), plus layering tests |
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
- Coverage must stay at or above 95% lines, 85% branches and 85% lines per file (`tools/quality.config.json`). New code comes with its tests.
- `tests/CurveRisk.Ai.Tests/ArchitectureTests.cs` enforces the dependency rules below on the compiled assemblies. A failure there means the design rule was broken, not that the test needs updating.
- The suppression budget is 1 and it is used. Any new `#pragma`, `SuppressMessage`, `NOSONAR` or `eslint-disable` fails the gate.
- For a change of more than a few files, get a `code-reviewer` pass on the diff before reporting it done, and a `security-reviewer` pass when it touches input handling, auth, secrets, files or the network. Reviews advise; the gate decides.
- After changing a review skill or reviewer brief, re-run `benchmarks/reviewer` and compare with its baseline.
- `DateTime.Now`, `Thread.Sleep`, `.Result` and `.Wait()` are banned: inject `TimeProvider`, and await.

## API rules

- Endpoints bind, call one service method and shape the response. No logic, no `DbContext`, no pricing types in `Endpoints/`; an architecture test enforces it.
- A breaking contract change gets a new route group under `/api/v2`; `/api/v1` keeps working. Use the `add-endpoint` skill.
- Caller mistakes are `ApiException` subclasses, each with one status and one stable problem type. Never return a 500 for something the caller did.
- A new endpoint comes with tests for the happy path and every documented error, driven through `ApiFactory` (the real pipeline, nothing mocked).
- The web page sets text with `textContent` only. Trade descriptions are untrusted.
- After a change to the page, run it (`.claude/launch.json` has the configuration) and click through it; a unit test cannot tell you the page works.

## Working rules

- Files under `evals/datasets/`, `tests/**/golden/`, `**/Generated/` and `*.verified.*` are blocked by a hook. If one of them looks wrong, stop and say so; do not work around the block.
- A failing eval or test is information about the code. Fix the code, or report that the expectation is wrong and why.
- Quant code states its conventions in the signature or the type: day count, compounding, units (percent vs fraction vs bp), sign. `ScenarioShock` and `RiskReport` show the style.
- Async all the way with `CancellationToken`; `ConfigureAwait(false)` in library code.
- New behaviour comes with a test that fails without it. For graders and guards, also a test showing they can fail.
- `CurveRisk.Analytics` targets `netstandard2.0` (PolySharp supplies `init` and records): no `Span`-only APIs, no default interface members, no `DateOnly`, no `Math.Clamp`. It stays pure: no I/O, no async, no reference to any other project. An architecture test enforces this.
- Inside the pricing library rates are fractions (`0.0378`) and time is ACT/365F years. Percent and basis points exist only at the engine and tool boundary, and the name says so.
- A change to pricing code gets a `quant-reviewer` pass before it is reported done.
- When you touch the Claude API surface, read the `claude-api` skill first; SDK type names are not guessable.

## Skills and subagents

General software engineering skills in `.claude/skills/`. Use them on every change, in any part of the codebase:

- `clean-code` before writing or modifying code. Run its size check on what you touched: `node .claude/skills/clean-code/scripts/size-report.mjs src evals tests` must report no findings.
- `unit-testing` when writing tests or raising coverage.
- `review-code` before reporting a change as done; `review-security` when the change touches input handling, auth, secrets, files or the network.
- `refactor-safely` for structural changes; `debug-systematically` when something fails.

Project skills, for work specific to this codebase: `add-copilot-tool`, `run-copilot-evals`, `add-endpoint`, `ef-migration`, `add-instrument`, `numerical-validation`, `quant-review`.

Subagents in `.claude/agents/` review with fresh context. General: `code-reviewer`, `security-reviewer`, `test-author`, `api-contract-guardian`. Project: `guardrail-reviewer`, `quant-reviewer`. Give a reviewer the diff and the requirement, not your conclusion.
