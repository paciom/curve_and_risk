# Curve & Risk

[![ci](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml/badge.svg)](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml)
[![codeql](https://github.com/paciom/curve_and_risk/actions/workflows/codeql.yml/badge.svg)](https://github.com/paciom/curve_and_risk/actions/workflows/codeql.yml)
[![OpenSSF Scorecard](https://api.scorecard.dev/projects/github.com/paciom/curve_and_risk/badge)](https://scorecard.dev/viewer/?uri=github.com/paciom/curve_and_risk)

**AI agents write the code. Engineering makes it trustworthy.**

## ⚡ At a glance

**One engineer. A team of AI agents. A production-shaped pricing and risk platform, with the test evidence to back it.**

| 🧪 99.9% / 98% | 🎯 92% | 🛡️ 0 | 🤖 677 |
|:---:|:---:|:---:|:---:|
| **line / branch coverage**, enforced as a gate | **of 1,483 injected faults caught** by the tests (mutation score), up from a first baseline of 55.6% | **analyzer findings**, and one suppressed rule in the whole codebase, with a budget that blocks a second | **tests**, all offline: 655 .NET and 22 on the agent guard rails |
| [📄 coverage report](docs/reports/coverage.md) | [📄 mutation report](docs/reports/mutation.md) | enforced at [build](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml); scanned by [CodeQL](https://github.com/paciom/curve_and_risk/actions/workflows/codeql.yml) | [📄 test report](docs/reports/coverage.md#tests) |

[![Stryker.NET mutation report: 1,365 mutants detected, 117 survived](docs/reports/mutation-report.png)](docs/reports/mutation.md)

The reports are snapshots of the latest full local run, committed so they open in one click. Live results are on the [ci](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml) and [mutation](https://github.com/paciom/curve_and_risk/actions/workflows/mutation.yml) workflow runs. [How each gate works](docs/quality-gates.md).

### AI engineering on show

- 🔁 **Agent loop engineering.** A [136-line loop](src/CurveRisk.Copilot/CopilotAgent.cs) with a call limit, a spend cap, human approval for writes and one bounded self-repair.
- 🔢 **Hallucination control that is code, not a prompt.** The model never originates a number: [every figure in an answer](src/CurveRisk.Copilot/NumericGrounding.cs) must trace to a tool result or the answer is withheld.
- 🧰 **Tool design and MCP.** [One tool catalog](src/CurveRisk.Ai.Tools/ToolCatalog.cs) serves both the in-product Copilot and an [MCP server](src/CurveRisk.Mcp) for external agents. Write tools sit behind an approval gate.
- 📏 **Evals.** A [dataset with deterministic graders](evals/) for the product agent, and a [benchmark](benchmarks/reviewer/) that scores the AI reviewers against planted defects: 12 of 12 found, no false alarms. The graders have tests showing they can fail.
- 🧠 **Context and prompt engineering.** A small always-loaded [`CLAUDE.md`](CLAUDE.md), thirteen on-demand [skills](.claude/skills/), a cache-stable system prompt, and untrusted text kept apart from instructions.
- 👥 **Multi-agent orchestration.** Six [reviewer subagents](.claude/agents/) that start from a clean context, and parallel agents in isolated worktrees: one session took the mutation score from 70% to 92% and surfaced a real bug.
- 🚧 **Guard rails for the coding agent itself.** [Hooks](.claude/hooks/) block edits to expected answers, thresholds and the hooks themselves, and verify the build before the agent may stop.
- 🔌 **Provider-independent and observable.** The SDK lives behind [one interface](src/CurveRisk.Copilot/IModelClient.cs), so the agent is tested offline; spans, token usage and cost are emitted as OpenTelemetry GenAI telemetry.

---

A .NET 10 pricing and risk platform built agent-first, where every rule that matters is enforced by something other than a prompt: a hook, a compiler error, a test, a threshold, or a human approval.

*This page is about the software and AI engineering. The financial domain is in [FINANCE.md](FINANCE.md).*

## What this demonstrates

**Agent skills written as engineering procedures.** Thirteen [skills](.claude/skills/). Six are general: [clean code](.claude/skills/clean-code/SKILL.md), [unit testing](.claude/skills/unit-testing/SKILL.md), [code review](.claude/skills/review-code/SKILL.md), [security review](.claude/skills/review-security/SKILL.md), [refactoring](.claude/skills/refactor-safely/SKILL.md), [debugging](.claude/skills/debug-systematically/SKILL.md). Each step has a check, and each rule carries its reason.

**Context engineering.** A small always-loaded [`CLAUDE.md`](CLAUDE.md), skills that load on demand, [reviewer subagents](.claude/agents/) that start from a clean context so the author cannot anchor them, and a hard line between trusted and untrusted text inside the product's own agent.

**Loop engineering.** A [136-line agent loop](src/CurveRisk.Copilot/CopilotAgent.cs) with a call limit, a spend cap, human approval for writes, verification of every answer and one bounded self-repair. Around it, a development loop where [hooks](.claude/hooks/) block, format and verify the coding agent's work.

**Prompt engineering, and its limits.** [Prompts](src/CurveRisk.Copilot/Prompts/system.md) that explain why. Then a control outside the prompt for everything that must hold, because a prompt asks and does not enforce.

**Guard rails an agent cannot argue with.** The coding agent is blocked from editing expected answers, thresholds and its own hooks. During this build it tried to change its own stop hook, and the hook refused.

**Measured AI.** An [eval suite](evals/) with deterministic graders for the product agent, and a [benchmark](benchmarks/reviewer/) that scores the AI reviewers against planted defects.

**API design.** [Versioned REST endpoints](src/CurveRisk.Api/Endpoints/ApiEndpoints.cs) with typed [contracts](src/CurveRisk.Contracts/V1/Contracts.cs) on .NET Standard 2.0, idempotent creation, ETag concurrency, a background job resource, RFC 9457 errors, OpenAPI, and EF Core on PostgreSQL.

**Conventional quality engineering underneath.** [One command](tools/check.mjs) runs nine gates locally, at push and in CI. AI review sits on top; it never decides whether a build passes.

## Proof it works

**An AI reviewer broke the AI-written guard rail.** With 88 tests green, an independent reviewer subagent found 16 ways around a safety check and a write path with no approval. Each became a failing test, then a fix.

**Static analysis caught what AI review missed.** Switching the analyzers on found 12 violations in code that had already been AI-reviewed, including a property with cyclomatic complexity 21. All fixed in code, none suppressed.

**The code failed its own standard and was refactored.** The size check flagged an 88-line agent loop. It was split into three small classes with every test green before and after.

**Coverage flattered the tests, and mutation testing said so.** With 96.8% of lines covered, the first mutation run caught only 55.6% of injected faults: tests were executing code without asserting on it. The score is now measured, published and tracked. Survivors were then worked through file by file, each one answered with a test of the behaviour it exposed, and it stands at 92.0%; most of what remains cannot be detected by any test. Writing those tests found a request that returned a 500 where it owed the caller a 409, now fixed.

**An independent reviewer re-derived the numbers.** A reviewer subagent rebuilt the demo curve from scratch in a separate language and matched the pricing library to the cent. It then found two inputs the library would have silently mispriced, a swap already under way and a deposit that had already started. Both are now refused with a clear error, with tests.

**Running it found what the tests could not.** All API tests were green, then the page was opened in a browser and the first click returned a 500: a fresh run had no database tables, because tests created the schema and a real start-up did not. Fixed, with a test for the opposite case.

**The reviewers earned their place.** Scored against ten planted defects, both found all of theirs and raised no false alarm. One also found a real bug in a sample that was meant to be clean.

## How quality is enforced

```text
edit      analyzers as compile errors · agent hooks (protect, format)
finish    the agent cannot end a turn on a broken build or failing tests
commit    format · size limits · lint · links · suppression budget
push      + build · tests · coverage (line, branch, per file) · guard-rail tests
PR        + secret scan · CodeQL · workflow lint · PostgreSQL tests · container build, smoke test and scan
main      + publish the tested image to the container registry · OpenSSF Scorecard
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
| The container pipeline | [`container.yml`](.github/workflows/container.yml), [`docs/deployment.md`](docs/deployment.md) |

## Status

Built: the pricing library, a versioned REST API with persistence, a small web page, the AI layer (MCP server, Copilot, evals), the agent harness and the quality pipeline. Run it with `dotnet run --project src/CurveRisk.Api` and open the URL it prints.

[Planned](PLAN.md), not built: database migrations, market-data imports, Aspire orchestration, and the step that deploys the published image to a cloud.

Not yet verified: the product agent has not been run against a live model (no API key yet), so its evals have no baseline; the PostgreSQL job has not run in CI; the pricing library is checked by closed forms and an independent re-derivation, not yet against QuantLib.

**Stack:** .NET 10 and .NET Standard 2.0 · C# · ASP.NET Core · EF Core · PostgreSQL · xUnit v3 · Anthropic SDK · Model Context Protocol · OpenTelemetry · Stryker.NET · SonarAnalyzer · CodeQL · Docker · GitHub Actions · Claude Code
