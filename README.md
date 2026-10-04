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
| **97.8% line, 91.6% branch** | coverage, enforced as a gate |
| **71.6% mutation score** | the honest measure of test strength: 1,045 mutants, up from a first baseline of 55.6% |
| **269 tests** | 247 .NET and 22 on the agent guard rails; no network, no credentials |
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

**Conventional quality engineering underneath.** [One command](tools/check.mjs) runs nine gates locally, at push and in CI. AI review sits on top; it never decides whether a build passes.

## Proof it works

**An AI reviewer broke the AI-written guard rail.** With 88 tests green, an independent reviewer subagent found 16 ways around a safety check and a write path with no approval. Each became a failing test, then a fix.

**Static analysis caught what AI review missed.** Switching the analyzers on found 12 violations in code that had already been AI-reviewed, including a property with cyclomatic complexity 21. All fixed in code, none suppressed.

**The code failed its own standard and was refactored.** The size check flagged an 88-line agent loop. It was split into three small classes with every test green before and after.

**Coverage flattered the tests, and mutation testing said so.** With 96.8% of lines covered, the first mutation run caught only 55.6% of injected faults: tests were executing code without asserting on it. The score is now measured, published and tracked, and stands at 71.6%.

**An independent reviewer re-derived the numbers.** A reviewer subagent rebuilt the demo curve from scratch in a separate language and matched the pricing library to the cent. It then found two inputs the library would have silently mispriced, a swap already under way and a deposit that had already started. Both are now refused with a clear error, with tests.

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
| A guard rail and its tests | [`protect-paths.mjs`](.claude/hooks/protect-paths.mjs), [`protect-paths.test.mjs`](tests/hooks/protect-paths.test.mjs) |
| Tests proving the graders can fail | [`EvalHarnessTests.cs`](tests/CurveRisk.Ai.Tests/EvalHarnessTests.cs) |
| Architecture rules as tests | [`ArchitectureTests.cs`](tests/CurveRisk.Ai.Tests/ArchitectureTests.cs) |
| What went wrong and how it was caught | [`docs/ai-workflow.md`](docs/ai-workflow.md) |
| Every claim, with its file | [`docs/ai-engineering.md`](docs/ai-engineering.md) |
| Every gate and threshold | [`docs/quality-gates.md`](docs/quality-gates.md) |

## Status

Built: the pricing library, the engine that exposes it, the AI layer, the agent harness and the quality pipeline. [Planned](PLAN.md): REST API, database and UI.

Not yet done: the product agent has not been run against the live model (no API key yet), so its evals have no baseline; SonarQube Cloud needs an account; branch rules need switching on in GitHub.

**Stack:** .NET 10 and .NET Standard 2.0 · C# · xUnit v3 · Anthropic SDK · Model Context Protocol · OpenTelemetry · Stryker.NET · SonarAnalyzer · CodeQL · GitHub Actions · Claude Code
