# Curve & Risk

[![ci](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml/badge.svg)](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml)
[![codeql](https://github.com/paciom/curve_and_risk/actions/workflows/codeql.yml/badge.svg)](https://github.com/paciom/curve_and_risk/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/paciom/curve_and_risk/badge)](https://scorecard.dev/viewer/?uri=github.com/paciom/curve_and_risk)

**AI agents write the code. Engineering makes it trustworthy.**

A .NET 10 pricing and risk platform built agent-first, where every rule that matters is enforced by something other than a prompt: a hook, a compiler error, a test, a threshold, or a human approval.

*This page is about the software and AI engineering. The financial domain is in [FINANCE.md](FINANCE.md).*

## The numbers

| | |
|---|---|
| **98.2% line, 91.9% branch** | coverage, enforced as a gate |
| **70.2% mutation score** | the honest measure of test strength: 1,426 mutants, up from a first baseline of 55.6% |
| **315 tests** | 293 .NET and 22 on the agent guard rails; no network, no credentials |
| **0 analyzer findings** | SonarQube rules, .NET analyzers, complexity limits and banned APIs, all compile errors |
| **12 of 12** | planted defects found by the AI reviewers in a scored benchmark, with no false alarms |
| **1** | rule suppression in the whole codebase, justified in writing, with a budget that blocks a second |

## What this demonstrates

**Agent skills written as engineering procedures.** Thirteen [skills](.claude/skills/). Six are general: [clean code](.claude/skills/clean-code/SKILL.md), [unit testing](.claude/skills/unit-testing/SKILL.md), [code review](.claude/skills/review-code/SKILL.md), [security review](.claude/skills/review-security/SKILL.md), [refactoring](.claude/skills/refactor-safely/SKILL.md), [debugging](.claude/skills/debug-systematically/SKILL.md). Each step has a check, and each rule carries its reason.

**Context engineering.** A small always-loaded [`CLAUDE.md`](CLAUDE.md), skills that load on demand, [reviewer subagents](.claude/agents/) that start from a clean context so the author cannot anchor them, and a hard line between trusted and untrusted text inside the product's own agent.

**Loop engineering.** A [130-line agent loop](src/CurveRisk.Copilot/CopilotAgent.cs) with a call limit, a spend cap, human approval for writes, verification of every answer and one bounded self-repair. Around it, a development loop where [hooks](.claude/hooks/) block, format and verify the coding agent's work.

**Prompt engineering, and its limits.** [Prompts](src/CurveRisk.Copilot/Prompts/system.md) that explain why. Then a control outside the prompt for everything that must hold, because a prompt asks and does not enforce.

**Guard rails an agent cannot argue with.** The coding agent is blocked from editing expected answers, thresholds and its own hooks. During this build it tried to change its own stop hook, and the hook refused.

**Measured AI.** An [eval suite](evals/) with deterministic graders for the product agent, and a [benchmark](benchmarks/reviewer/) that scores the AI reviewers against planted defects.

**API design.** [Versioned REST endpoints](src/CurveRisk.Api/Endpoints/ApiEndpoints.cs) with typed [contracts](src/CurveRisk.Contracts/V1/Contracts.cs) on .NET Standard 2.0, idempotent creation, ETag concurrency, a background job resource, RFC 9457 errors, OpenAPI, and EF Core on PostgreSQL.

**Conventional quality engineering underneath.** [One command](tools/check.mjs) runs nine gates locally, at push and in CI. AI review sits on top; it never decides whether a build passes.

## Proof it works

**An AI reviewer broke the AI-written guard rail.** With 88 tests green, an independent reviewer subagent found 16 ways around a safety check and a write path with no approval. Each became a failing test, then a fix.

**Static analysis caught what AI review missed.** Switching the analyzers on found 12 violations in code that had already been AI-reviewed, including a property with cyclomatic complexity 21. All fixed in code, none suppressed.

**The code failed its own standard and was refactored.** The size check flagged an 88-line agent loop. It was split into three small classes with every test green before and after.

**Coverage flattered the tests, and mutation testing said so.** With 96.8% of lines covered, the first mutation run caught only 55.6% of injected faults: tests were executing code without asserting on it. The score is now measured, published and tracked, and stands at 70.2%.

**An independent reviewer re-derived the numbers.** A reviewer subagent rebuilt the demo curve from scratch in a separate language and matched the pricing library to the cent. It then found two inputs the library would have silently mispriced, a swap already under way and a deposit that had already started. Both are now refused with a clear error, with tests.

**Running it found what the tests could not.** All API tests were green, then the page was opened in a browser and the first click returned a 500: a fresh run had no database tables, because tests created the schema and a real start-up did not. Fixed, with a test for the opposite case.

**The reviewers earned their place.** Scored against ten planted defects, both found all of theirs and raised no false alarm. One also found a real bug in a sample that was meant to be clean.

## How quality is enforced

```text
edit      analyzers as compile errors · agent hooks (protect, format)
finish    the agent cannot end a turn on a broken build or failing tests
commit    format · size limits · lint · links · suppression budget
push      + build · tests · coverage (line, branch, per file) · guard-rail tests
PR        + secret scan · CodeQL · workflow lint · OpenSSF Scorecard
weekly    mutation testing · dependency updates
```

```bash
node tools/check.mjs
```

AI review, failure triage and a weekly security sweep run locally in Claude Code today. The same steps exist as [workflows](.github/workflows/) and switch on with an API key. They advise; they never block a merge.

## Start here

| To see | Open |
|---|---|
| A skill file | [`clean-code/SKILL.md`](.claude/skills/clean-code/SKILL.md) |
| The agent loop | [`CopilotAgent.cs`](src/CurveRisk.Copilot/CopilotAgent.cs) |
| The REST API | [`ApiEndpoints.cs`](src/CurveRisk.Api/Endpoints/ApiEndpoints.cs), [`TradeApiTests.cs`](tests/CurveRisk.Api.Tests/TradeApiTests.cs) |
| The pricing library | [`CurveBootstrapper.cs`](src/CurveRisk.Analytics/Curves/CurveBootstrapper.cs), [`FINANCE.md`](FINANCE.md) |
| A guard rail and its tests | [`protect-paths.mjs`](.claude/hooks/protect-paths.mjs), [`protect-paths.test.mjs`](tests/hooks/protect-paths.test.mjs) |
| Tests proving the graders can fail | [`EvalHarnessTests.cs`](tests/CurveRisk.Ai.Tests/EvalHarnessTests.cs) |
| Architecture rules as tests | [`ArchitectureTests.cs`](tests/CurveRisk.Ai.Tests/ArchitectureTests.cs) |
| What went wrong and how it was caught | [`docs/ai-workflow.md`](docs/ai-workflow.md) |
| Every claim, with its file | [`docs/ai-engineering.md`](docs/ai-engineering.md) |
| Every gate and threshold | [`docs/quality-gates.md`](docs/quality-gates.md) |

## Status

Built: the pricing library, a versioned REST API with persistence, a small web page, the AI layer (MCP server, Copilot, evals), the agent harness and the quality pipeline. Run it with `dotnet run --project src/CurveRisk.Api` and open the URL it prints.

[Planned](PLAN.md), not built: database migrations, market-data imports, Aspire orchestration, cloud deployment.

Not yet verified: the product agent has not been run against a live model (no API key yet), so its evals have no baseline; the PostgreSQL job has not run in CI; the pricing library is checked by closed forms and an independent re-derivation, not yet against QuantLib.

**Stack:** .NET 10 and .NET Standard 2.0 · C# · ASP.NET Core · EF Core · PostgreSQL · xUnit v3 · Anthropic SDK · Model Context Protocol · OpenTelemetry · Stryker.NET · SonarAnalyzer · CodeQL · GitHub Actions · Claude Code
