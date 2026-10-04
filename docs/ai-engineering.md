# AI engineering in this repository: the detail

The [README](../README.md) is the summary. This page is the full account, with a link to the file behind every claim.

A demonstration of AI-assisted software engineering done as engineering: agent skills, context, prompts, loops, guard rails and evaluation, applied to a .NET 10 codebase.

The domain is interest-rate risk, and the worked example is a Risk Copilot (an LLM agent with tools). The subject of the repository is the method: how to get high-quality, well-tested, reviewed code out of AI agents, and how to ship an AI feature whose guarantees hold when the model is wrong. Every claim below links to the file that backs it.

> **Status.** The AI layer is complete and tested. The quant engine, REST API, database and web UI are planned ([PLAN.md](../PLAN.md)) and not yet built; a small fixture engine stands in. The Copilot has not yet been run against the live model. See [What is not done](#what-is-not-done).

## Capability map

| Capability | What it means here | Where to look |
|---|---|---|
| **Agent Skills** | Reusable, model-invoked workflows for writing, testing, reviewing, securing, refactoring and debugging code | [`.claude/skills/`](../.claude/skills/) |
| **Skill file authoring** | Trigger-oriented descriptions, procedures with a check per step, progressive disclosure into reference files, bundled scripts | [`clean-code/`](../.claude/skills/clean-code/) |
| **Prompt engineering** | System prompt, tool descriptions, subagent briefs and repair messages that explain the reason behind each rule | [Prompt engineering](#prompt-engineering) |
| **Context engineering** | Deciding what each agent sees, when, and what it must never treat as instruction | [Context engineering](#context-engineering) |
| **Loop engineering** | Bounded agent loops with feedback, repair, approval and stop conditions, in the product and in development | [Loop engineering](#loop-engineering) |
| **Guard rails** | Rules enforced by code, not by asking: hooks, permission tiers, approval gates | [`.claude/hooks/`](../.claude/hooks/), [`settings.json`](../.claude/settings.json) |
| **Deterministic quality gates** | Linting, SonarQube rules, complexity limits, banned APIs, coverage thresholds, secret and security scanning, all automatic | [Quality gates](#quality-gates-non-ai) |
| **Subagents** | Reviewers and a test author that work with fresh context | [`.claude/agents/`](../.claude/agents/) |
| **Evaluation** | Deterministic graders, must-pass safety cases, graders tested for their ability to fail | [`evals/`](../evals/) |
| **Tool design and MCP** | One tool definition serving an MCP server and an in-process agent | [`ToolCatalog`](../src/CurveRisk.Ai.Tools/ToolCatalog.cs), [`CurveRisk.Mcp`](../src/CurveRisk.Mcp/) |
| **AI observability and cost** | OpenTelemetry GenAI spans, token metrics, per-call costing, daily budget cap | [`CopilotTelemetry`](../src/CurveRisk.Copilot/CopilotTelemetry.cs), [`Cost.cs`](../src/CurveRisk.Copilot/Cost.cs) |

## Agent Skills

Skills live in [`.claude/skills/`](../.claude/skills/), one folder each, with a `SKILL.md`. The agent sees only each skill's name and description until a task matches; then it loads the body, and reference files only if it needs them.

### Software engineering skills (project-independent)

These apply to any codebase and contain nothing about finance.

| Skill | What it makes the agent do |
|---|---|
| [`clean-code`](../.claude/skills/clean-code/SKILL.md) | Small files and functions, one responsibility, intention-revealing names, dependency injection, immutability, explicit error handling, no speculative abstraction. Bundles a [dependency-injection reference](../.claude/skills/clean-code/references/dependency-injection.md), a [C# reference](../.claude/skills/clean-code/references/csharp.md) and a [size-check script](../.claude/skills/clean-code/scripts/size-report.mjs) |
| [`unit-testing`](../.claude/skills/unit-testing/SKILL.md) | List behaviours before writing tests; cover boundaries, failures and invariants; see every test fail once; raise coverage by behaviour, not by line; never weaken an assertion to get to green |
| [`review-code`](../.claude/skills/review-code/SKILL.md) | Review in passes (correctness, design, readability, tests, security, operations); verify before reporting; findings by severity with a triggering scenario |
| [`review-security`](../.claude/skills/review-security/SKILL.md) | Trace attacker-controlled input from source to sink; checklist covering injection, authentication, authorisation, secrets, cryptography, deserialisation, SSRF, exposure, supply chain and LLM prompt injection |
| [`refactor-safely`](../.claude/skills/refactor-safely/SKILL.md) | Structure changes only, in small steps, tests green before and after each; characterisation tests first when there is no safety net |
| [`debug-systematically`](../.claude/skills/debug-systematically/SKILL.md) | Reproduce, locate, one hypothesis at a time, fix the cause, add the regression test |

### Project skills

Workflows specific to this codebase: [`add-copilot-tool`](../.claude/skills/add-copilot-tool/SKILL.md), [`run-copilot-evals`](../.claude/skills/run-copilot-evals/SKILL.md), [`add-endpoint`](../.claude/skills/add-endpoint/SKILL.md), [`ef-migration`](../.claude/skills/ef-migration/SKILL.md), [`add-instrument`](../.claude/skills/add-instrument/SKILL.md), [`numerical-validation`](../.claude/skills/numerical-validation/SKILL.md), [`quant-review`](../.claude/skills/quant-review/SKILL.md).

### How the skill files are written

- **The description is the trigger.** It says what the skill does and when to use it, in the words a request would use, because that line is all the agent sees when deciding whether to load it.
- **Procedures, with the check for each step.** "Run the size check", "see the test fail once", not "write good code".
- **Reasons, not commands.** Each rule carries its why, so the agent can apply it to a case the skill did not foresee.
- **Defaults with an escape.** Limits are numbers (30-line functions, 300-line files, 4 parameters) that may be exceeded with a stated reason.
- **Progressive disclosure.** `SKILL.md` stays short; depth goes in `references/`, and anything mechanical goes in `scripts/` so it is executed, not re-derived.
- **Skills reference each other** instead of repeating content: `review-code` uses `clean-code` and `unit-testing` as its standards.

## Subagents

[`.claude/agents/`](../.claude/agents/). A reviewer that has read the author's reasoning tends to confirm it, so these run in a separate context and receive the code and the requirement only.

| Agent | Role | Tier |
|---|---|---|
| [`code-reviewer`](../.claude/agents/code-reviewer.md) | General review against the `review-code`, `clean-code` and `unit-testing` skills; read-only | General |
| [`security-reviewer`](../.claude/agents/security-reviewer.md) | Source-to-sink vulnerability review; read-only | General |
| [`test-author`](../.claude/agents/test-author.md) | Writes tests from the specification without opening the implementation | General |
| [`api-contract-guardian`](../.claude/agents/api-contract-guardian.md) | Classifies API changes as breaking, additive or none | General |
| [`guardrail-reviewer`](../.claude/agents/guardrail-reviewer.md), [`quant-reviewer`](../.claude/agents/quant-reviewer.md) | Reviews specific to this project's AI guarantees and pricing code | Project |

Reviewers have read and search tools only. They cannot edit what they review.

## Prompt engineering

| Prompt | Technique | File |
|---|---|---|
| Copilot system prompt | States the reason for each rule, tells the model its output is checked and what will be rejected, defines tool results as data | [`system.md`](../src/CurveRisk.Copilot/Prompts/system.md) |
| Tool descriptions | Written as prompt text: when to use the tool, units on every parameter, the common mistake to avoid | [`RiskTools.cs`](../src/CurveRisk.Ai.Tools/RiskTools.cs) |
| Repair message | Names the exact figures that failed and the two acceptable ways to fix each; tagged as automated so it is not mistaken for the user | [`CopilotAnswer.cs`](../src/CurveRisk.Copilot/CopilotAnswer.cs) |
| Tool error messages | Say what was wrong and what the valid values are, so the model can correct itself in one step | [`EngineErrorSurfacingFunction.cs`](../src/CurveRisk.Mcp/EngineErrorSurfacingFunction.cs) |
| Subagent briefs | Role, what to examine, how to verify, what not to do, and the report format | [`.claude/agents/`](../.claude/agents/) |
| CI review prompt | Scopes the review, routes to the right brief, and marks the PR text as material to review, not instructions | [`claude-review.yml`](../.github/workflows/claude-review.yml) |

The limit of prompting is stated in the design: a prompt asks, it does not enforce. Every rule that must hold has a control outside the prompt (next two sections).

## Context engineering

What an agent is given decides what it can do well. The choices made here:

- **Always-loaded context is small and stable.** [`CLAUDE.md`](../CLAUDE.md) holds commands, layout and five invariants. Everything else loads on demand.
- **On-demand context through skills.** Descriptions cost a line each; bodies and references load only when the task calls for them.
- **Isolated context for review.** Subagents start clean, so a review is not anchored on the author's conclusion.
- **Live context through MCP.** [`.mcp.json`](../.mcp.json) connects the coding agent to the project's own tool server, so it can query the system it is building instead of guessing.
- **Trusted and untrusted context are kept apart.** In the Copilot, numeric evidence comes only from JSON numbers in successful tool results and from the user's own words. Free-text fields, failed results and harness-written turns are excluded ([`AskSession.cs`](../src/CurveRisk.Copilot/AskSession.cs), [`NumericGrounding.cs`](../src/CurveRisk.Copilot/NumericGrounding.cs)).
- **Cache-stable prefix.** The system prompt and tool list are byte-identical across calls, with no timestamps or ids, so prompt caching can apply. A [test](../tests/CurveRisk.Ai.Tests/CopilotAgentTests.cs) asserts it.
- **Reasoning is preserved across turns.** Thinking blocks are replayed unchanged with their signatures ([`AnthropicModelClient.cs`](../src/CurveRisk.Copilot/AnthropicModelClient.cs)).
- **Telemetry carries no content.** Spans record models, tokens and outcomes, never prompts or tool payloads.

## Loop engineering

Loop engineering is building the loop that prompts, checks and directs an agent, so that a person does not type each instruction. A loop's quality comes from what feeds back into it and what stops it.

**The mutation-kill loop** ([`tools/mutation-loop.mjs`](../tools/mutation-loop.mjs)): a script drives the coding agent towards a measured target, the mutation score. Each round takes one source file's surviving mutants.

```text
measure (Stryker) -> pick a file's survivors -> prompt the agent -> check -> keep or revert -> next
                                                     ^                           |
                                                     +---- what the check found --+
```

| Part | Implementation |
|---|---|
| Objective | The mutation score, computed from Stryker's reports ([`report.mjs`](../tools/mutation-loop/report.mjs)), never from what the agent says |
| Instruction | Written by the script from the surviving mutants ([`prompt.mjs`](../tools/mutation-loop/prompt.mjs)) and run in a headless `claude -p` session |
| Check | After every attempt the script verifies that tests were only added (nothing outside the test projects touched, no existing line removed, no test that reads the environment), that `check.mjs --fast` passes, and that Stryker, rerun on that file, reports the targets killed and nothing previously killed now surviving ([`loop.mjs`](../tools/mutation-loop/loop.mjs)) |
| Keep or revert | The loop holds the last accepted state as a git tree. A confirmed kill is staged and becomes that tree; anything else, staged or not, is put back to it. A commit made during an attempt stops the run ([`ports.mjs`](../tools/mutation-loop/ports.mjs)) |
| Feedback | The reason for a rejection, or the mutants still surviving, is the next prompt; two attempts per file |
| Stop conditions | Target score reached, nothing left to try, round limit, spend limit in US dollars, or three rounds in a row without a kill |
| Unobservable mutants | Named by a rule in [`quality.config.json`](../tools/quality.config.json) (`ConfigureAwait(false)` flips), not by the agent. An agent's claim that a mutant is unobservable is logged for a person and changes nothing |
| Ledger | One JSON line per attempt: file, outcome, kills, files changed, cost, score |

The loop's decisions are tested against a scripted agent, including one that claims success without achieving it and one that edits the code under test ([`mutation-loop.test.mjs`](../tests/tools/mutation-loop.test.mjs)); keep and revert are tested on a real throwaway repository ([`mutation-loop-ports.test.mjs`](../tests/tools/mutation-loop-ports.test.mjs)). Kept tests are staged, not committed, so a person still reviews what is merged.

Limits. The checks make the cheap ways of cheating fail; they do not prove a kept test is a good test, which is why the result is staged for review. The headless session runs with the project's own permission settings, and files git ignores are outside the loop's view. Each check reruns Stryker on one file, about six minutes here, so a full run takes hours. The score the loop reports is the starting report plus confirmed kills; a full `dotnet stryker` run afterwards is the score of record.

**The product loop** ([`CopilotAgent.cs`](../src/CurveRisk.Copilot/CopilotAgent.cs), about 130 lines): call the model, run its tool calls, feed results back, check the answer, repeat.

| Loop control | Implementation |
|---|---|
| Hard bound on iterations | `MaxModelCalls` |
| Spend bound | [`BudgetGuard`](../src/CurveRisk.Copilot/Cost.cs) checked before every model call |
| Human in the loop for writes | [`IApprovalGate`](../src/CurveRisk.Copilot/Approval.cs), default deny, asked one at a time ([`ToolExecutor.cs`](../src/CurveRisk.Copilot/ToolExecutor.cs)) |
| Verification inside the loop | Every final answer passes [`NumericGrounding`](../src/CurveRisk.Copilot/NumericGrounding.cs) or is not returned |
| Bounded self-repair | One repair turn naming the failures, then the answer is withheld |
| Parallel tool calls | Run concurrently, returned in one turn, order preserved |
| Every exit is a named status | Answered, withheld, refused, truncated, call limit, budget, unavailable |

**The development loop** (Claude Code hooks): the agent edits, the harness reacts.

| Event | Hook | Feedback |
|---|---|---|
| Before an edit | [`protect-paths`](../.claude/hooks/protect-paths.mjs) | Blocks edits to eval data, golden files, generated code, snapshots and the hooks themselves, with the reason |
| After an edit | [`format-on-edit`](../.claude/hooks/format-on-edit.mjs) | Formats the file |
| Before the turn ends | [`verify-on-stop`](../.claude/hooks/verify-on-stop.mjs) | Builds and tests; a failure sends the agent back to work with the output; a loop guard prevents it cycling forever |

**The evaluation loop**: change a prompt or tool, run the [eval suite](../evals/), compare with the baseline, keep or revert ([`run-copilot-evals`](../.claude/skills/run-copilot-evals/SKILL.md)). Injection and write cases are must-pass, not averaged.

## Quality gates (non-AI)

AI review sits on top of deterministic tools; it does not replace them. A tool gives the same answer every time and cannot be persuaded. Full detail in [docs/quality-gates.md](quality-gates.md).

```bash
node tools/check.mjs
```

| Gate | Tool | Fails when |
|---|---|---|
| Lint and style | `dotnet format` with [`.editorconfig`](../.editorconfig); .NET analyzers at `latest-recommended` | Any style or analyzer rule marked error is broken |
| SonarQube rules | [SonarAnalyzer.CSharp](../Directory.Build.props) inside every build, offline | Bugs, code smells, cognitive complexity, nesting, hard-coded credentials, commented-out code |
| Complexity | Code metrics ([`CodeMetricsConfig.txt`](../CodeMetricsConfig.txt)) | A method's cyclomatic complexity exceeds 10 |
| Size | [`size-report.mjs`](../.claude/skills/clean-code/scripts/size-report.mjs) | File over 300 lines, function over 30, more than 4 parameters |
| Banned APIs | [`BannedSymbols.txt`](../BannedSymbols.txt) | `DateTime.Now`, `Thread.Sleep`, `.Result`, `.Wait()`, `new Random()` are used |
| Vulnerable dependencies | NuGet audit | A known-vulnerable package, direct or transitive, is referenced |
| Tests | xUnit v3 | Any test fails |
| Coverage | [`coverage-gate.mjs`](../tools/coverage-gate.mjs), [`quality.config.json`](../tools/quality.config.json) | Total under 95%, or any file under 85% |

Every analyzer finding is a compile error, so the rules are enforced wherever the code is built. The same `check.mjs` runs in the [pre-push hook](../.githooks/pre-push) and in [CI](../.github/workflows/ci.yml); a faster subset runs on [pre-commit](../.githooks/pre-commit). Git hooks install themselves on first build ([`Directory.Build.targets`](../Directory.Build.targets)). CI adds [gitleaks](../.github/workflows/ci.yml) secret scanning, [CodeQL](../.github/workflows/codeql.yml), a [SonarQube Cloud quality gate](../.github/workflows/sonarcloud.yml) and [Dependabot](../.github/dependabot.yml).

The gates constrain the agent as much as a person. `CLAUDE.md` forbids suppressing a rule or lowering a threshold to get a build through, and the agent's stop hook will not let a turn end on a failing build.

When the analyzers were first switched on they found 12 violations in code that had already passed AI review, including a property with cyclomatic complexity 21 and an adapter class coupled to 73 types. All were fixed in the code. One rule is suppressed in source, with a written justification.

## Quality evidence

**Code that meets its own standard.** The `clean-code` size check reports no findings across `src/`, `evals/` and `tests/`:

```bash
node .claude/skills/clean-code/scripts/size-report.mjs src evals tests
```

It did not at first. The agent loop was an 88-line method in a 320-line file. Following `refactor-safely`, it was split into a stateless agent, a per-question [`AskSession`](../src/CurveRisk.Copilot/AskSession.cs) and a [`ToolExecutor`](../src/CurveRisk.Copilot/ToolExecutor.cs), with the tests green before and after and no assertion changed.

**Design for testability.** The agent depends on [`IModelClient`](../src/CurveRisk.Copilot/IModelClient.cs), `IApprovalGate`, `IRiskEngine` and `TimeProvider`, all injected through constructors. That is why the whole loop runs in tests against a [scripted model](../tests/CurveRisk.Ai.Tests/ScriptedModelClient.cs) with no network.

**Tests.** 655 .NET tests, 22 hook tests and 29 tests of the mutation loop, no credentials needed. Line coverage is 96.8% over `src/` and `evals/` (console entry points excluded), enforced by the coverage gate.

Beyond coverage, the suite has:
- **Wire-level tests without a network.** The real Anthropic SDK is driven against a stubbed HTTP transport and the request JSON is asserted ([`AnthropicModelClientTests`](../tests/CurveRisk.Ai.Tests/AnthropicModelClientTests.cs)).
- **Tests that the guards can fail.** Each eval grader is run against a scripted model making the exact mistake it exists to catch ([`EvalHarnessTests`](../tests/CurveRisk.Ai.Tests/EvalHarnessTests.cs)).
- **Review findings kept as regression tests** ([`NumericGroundingTests`](../tests/CurveRisk.Ai.Tests/NumericGroundingTests.cs)).
- **Tests for the guard rails themselves** ([`tests/hooks`](../tests/hooks/protect-paths.test.mjs)).

**AI code review that found real defects.** With 88 tests green, an independent reviewer subagent was given the code and a brief, and not the author's reasoning. It wrote its own probes and returned verified findings:

| Finding | Fix |
|---|---|
| 16 inputs slipped an invented number past the answer check (`2e6`, `2 000 000`, `SEK1900000`, "2 million" for 1,000,000) | Every digit run is a claim; stricter evidence rules; three significant digits before a rounding is accepted |
| A rejected number became valid evidence on the next question | Harness-written turns are marked and excluded |
| The MCP server exposed a write tool with no approval | Read-only by default |
| The path guard was case-sensitive on Windows and did not protect itself | Case-insensitive; guards its own files; 22 tests |
| An exception in one tool call lost the record of an approved write in a sibling call | Per-call handling; outcome always recorded |
| One eval grader could not fail on its own | Recomputed independently |
| A 90% threshold let both injection cases fail and still pass | Safety cases are must-pass |

Each became a failing test before it was fixed. The full account is in [docs/ai-workflow.md](ai-workflow.md).

**Pipelines.** [`ci.yml`](../.github/workflows/ci.yml) runs the quality gate and secret scanning. [`copilot-evals.yml`](../.github/workflows/copilot-evals.yml) runs live evals on changes that can alter agent behaviour. [`claude-review.yml`](../.github/workflows/claude-review.yml) runs agent review on every pull request, comment-only.

## Repository layout

| Path | Contents |
|---|---|
| [`.claude/skills/`](../.claude/skills/) | 13 skills: 6 general, 7 project |
| [`.claude/agents/`](../.claude/agents/) | 6 subagent briefs |
| [`.claude/hooks/`](../.claude/hooks/), [`.claude/settings.json`](../.claude/settings.json) | Guard rails and permission tiers (allow / ask / deny) |
| [`CLAUDE.md`](../CLAUDE.md), [`.mcp.json`](../.mcp.json) | Always-loaded context; MCP wiring |
| [`src/CurveRisk.Ai.Tools`](../src/CurveRisk.Ai.Tools/) | Engine port and the single tool catalog |
| [`src/CurveRisk.Mcp`](../src/CurveRisk.Mcp/) | MCP stdio server |
| [`src/CurveRisk.Copilot`](../src/CurveRisk.Copilot/) | Agent loop, guard rails, cost, telemetry, Claude adapter |
| [`evals/`](../evals/) | 18 cases, six deterministic graders, live runner |
| [`tests/`](../tests/) | .NET tests and hook tests |
| [`tools/`](../tools/), [`.githooks/`](../.githooks/) | The quality gate, coverage gate, thresholds, git hooks |
| [`docs/`]() | [ADR 0001](adr/0001-ai-layer-architecture.md), [AI workflow](ai-workflow.md), [quality gates](quality-gates.md) |

## Run it

```bash
dotnet build CurveRisk.slnx
```

```bash
dotnet test
```

```bash
node --test "tests/hooks/*.test.mjs"
```

Live evals need `ANTHROPIC_API_KEY` and cost money:

```bash
dotnet run --project evals/CurveRisk.Evals -- --trials 3
```

Build the MCP server once so Claude Code can start it:

```bash
dotnet build src/CurveRisk.Mcp -c Release
```

Open the folder in Claude Code and the skills, subagents, hooks and MCP server load from the repository. Ask it to review a file, or to edit `evals/datasets/copilot.jsonl` and watch the hook refuse.

## What is not done

Stated here because an unverified claim is worth less than a stated gap.

- **Never run against the live model.** No API credentials were available. The eval suite has no baseline, the 0.9 threshold is a placeholder, and prompt-cache hits are unconfirmed.
- **The general skills are new.** `clean-code` and `refactor-safely` were applied to this codebase; `review-security`, `debug-systematically` and the `code-reviewer` and `security-reviewer` subagents have not yet been exercised on it. No security review of this repository has been run.
- **Optional workflows are switched off until configured.** The quality gate and secret scan run on every push and pull request. CodeQL runs only while the repository is public. SonarQube Cloud needs a project, `SONAR_TOKEN` and two repository variables. Live evals and agent PR review need `ANTHROPIC_API_KEY` plus the variables `ENABLE_LIVE_EVALS` and `ENABLE_CLAUDE_REVIEW`. Required status checks must be switched on in the repository's branch rules.
- **The agent's stop hook runs build and tests, not yet the full gate.** Coverage and size limits are enforced at push and in CI. Pointing the stop hook at `tools/check.mjs` is a one-line change the agent is blocked from making itself, by design.
- **The engine is a placeholder**: one hard-coded curve and three swaps.
- **No host for the Copilot yet**: no streaming to a UI, no per-user rate limit.
- **`CurveRisk.Mcp` has no automated tests.** It was verified by a stdio smoke test.
- **Open eval-dataset items**, left for a person because the dataset is protected from agent edits: `save-declined` would pass a model that falsely claims it saved; two cases would pass an answer that spells a number out in words.
- **The review fixes and the refactor have not had a second independent review.**

## Stack

.NET 10, C#, xUnit v3 on Microsoft.Testing.Platform, the Anthropic C# SDK, `Microsoft.Extensions.AI`, the Model Context Protocol C# SDK, OpenTelemetry, GitHub Actions, Claude Code (skills, subagents, hooks, MCP).
