# How agents are used in this repository

This project is built with coding agents and ships one. This page explains the working method, where each piece lives, and what happened when it was first used. For the design decisions see [ADR 0001](adr/0001-ai-layer-architecture.md).

## The idea

A model's output is a proposal. What makes it usable is everything around the model: what it is told, what it can touch, what checks its work, and what is measured afterwards. Prompting is one of those four and the least reliable, so anything that has to hold is enforced somewhere else.

| Concern | Left to the prompt | Enforced by |
|---|---|---|
| Copilot quotes only engine figures | "Every figure you state must be a value returned by a tool" | `NumericGrounding` on every answer; withheld if it fails twice |
| Copilot does not write unasked | "Call it only when the user has asked" | `IApprovalGate`; default deny |
| Agent does not edit expected answers | "Do not edit golden files" in `CLAUDE.md` | `protect-paths` hook blocks the edit |
| Agent finishes with working code | "Run the tests" | `verify-on-stop` hook builds and tests before the turn can end |
| A prompt change did not make things worse | Reading a few transcripts | Eval suite with a threshold, in CI |

## Where things are

**For building the system (Claude Code)**

| Path | Purpose |
|---|---|
| `CLAUDE.md` | Commands, layout, the five invariants, working rules |
| `.claude/settings.json` | Permissions (allow / ask / deny) and hook wiring |
| `.claude/hooks/protect-paths.mjs` | PreToolUse: blocks edits to eval datasets, golden files, generated code, accepted snapshots |
| `.claude/hooks/format-on-edit.mjs` | PostToolUse: formats the edited C# file |
| `.claude/hooks/verify-on-stop.mjs` | Stop: build and test when source changed; a failure keeps the agent working |
| `.claude/skills/` | Six general software engineering skills (`clean-code`, `unit-testing`, `review-code`, `review-security`, `refactor-safely`, `debug-systematically`) and seven project workflows |
| `.claude/agents/` | Six reviewer and author briefs that run with fresh context |
| `.mcp.json` | Gives the coding agent the project's own MCP server, so it can query the engine it is building |
| `.github/workflows/claude-review.yml` | The same reviewer briefs on every pull request; comment-only |

**In the product (Risk Copilot)**

| Path | Purpose |
|---|---|
| `src/CurveRisk.Ai.Tools` | `IRiskEngine` port; `ToolCatalog`, the single definition of each tool |
| `src/CurveRisk.Mcp` | MCP stdio server over the catalog |
| `src/CurveRisk.Copilot` | Agent loop, approval gate, grounding check, budget, telemetry, Anthropic adapter, system prompt |
| `evals/` | Dataset, graders, live runner; `copilot-evals.yml` runs it on relevant changes |
| `tests/CurveRisk.Ai.Tests` | 119 tests, no network: guardrails, loop, wire format, and tests that each grader can fail |
| `tests/hooks` | 22 tests of the path guard hook, run the way the harness runs it |

## The loop

1. **Specify.** State the behaviour and the check that will show it works before any code. For quant code the check is a reference value from an independent library. For Copilot behaviour it is an eval case.
2. **Plan.** For anything touching an invariant or a contract, write the approach down first (`PLAN.md`, an ADR). The plan is reviewed by a person; that is the cheapest point to catch a wrong direction.
3. **Implement** with a skill where one exists, so the steps and their checks are the same every time.
4. **Verify by running.** Compile, run the tests, exercise the real thing (the MCP server over stdio, the adapter against a stub transport). "It compiles" and "I read it and it looks right" are not verification.
5. **Review with fresh context.** A subagent gets the change and the requirement, not the author's explanation. An agent handed a conclusion tends to confirm it.
6. **Measure.** For Copilot changes, compare eval results before and after on the same dataset and trial count.
7. **Report what was and was not verified.** Unrun is stated as unrun.

## What happened on the first use

The AI layer itself was built this way in one agent session. These are the points where a check changed the outcome.

**The MCP smoke test found tool errors the model could not use.** The server built cleanly and listed its tools. Calling `price_trade` over stdio with an unknown trade id returned `An error occurred invoking 'price_trade'.` and nothing else: the MCP SDK hides exception messages by default. A model receiving that cannot correct itself. The fix (`EngineErrorSurfacingFunction`) passes engine validation messages through and still hides unexpected failures. Unit tests would not have found this; only calling the server the way a client does.

**The grounding check failed open on its first version.** The number pattern skipped any digits attached to a letter, to avoid treating `T-1001` as a figure. A review pass asked the opposite question (what does this let through?) and found `USD1,900,000`, `1900000USD` and `approx-1900000` were all invisible to it. Failing tests were written first, then the pattern was rewritten so that a digit run is a claim unless it matches a narrow identifier or tenor form. The identifier exemption was then tightened a second time when one of the new tests showed it still swallowed `approx-1900000`.

**A test expectation was wrong, and the code was left alone.** A test asserted that "1.6 million" must be rejected for an engine value of 1,672,655. The checker accepts it, because it allows truncation to the displayed digit as well as rounding. That behaviour is the documented design, so the test case was changed (to 1.5 million, which is rejected) and the reason recorded here. The alternative, tightening the tolerance until the test passed, would have made the check reject honest answers.

**A patch applied through the shell corrupted a source file.** An escaping layer turned `\b` in a regular expression into a backspace character. The file still compiled. Nine tests failed on the next run, which is what located it; the file was rewritten directly. This is the argument for running the suite after every change instead of at the end.

**An independent reviewer found what the author had missed.** With 88 tests passing, a subagent was given the `guardrail-reviewer` brief and the code, and not the author's reasoning. It wrote its own probe programs and came back with verified findings, among them:

- Sixteen answer strings that carried an invented number past the grounding check: `2e6`, `2 000 000`, `SEK1900000`, `.75 million`, `7 trillion`, `NZD-45,000`; "2 million" accepted for 1,000,000; a PV of 1,001 accepted because the trade id `T-1001` was in the evidence; 500 accepted because an attacker-written trade description contained it.
- The repair notice, which names the rejected number, was treated as user text on the next question, so a rejected number supported itself one turn later.
- The MCP server offered `save_scenario` with no approval at all, contradicting the invariant written in `CLAUDE.md` an hour earlier.
- The path guard was case-sensitive on a case-insensitive file system (`Evals/Datasets/` was editable) and did not protect itself.
- One eval grader could not fail independently: it re-read the agent's own verdict.
- A 90% pass threshold would have let both prompt-injection cases fail and still report success.

Each became a failing test first, then a fix: the checker now treats every digit run as a claim, takes numeric evidence only from JSON numbers and the user's words, and requires three significant digits before it accepts a rounding; harness-written turns are marked and excluded from evidence; the MCP server is read-only unless started with `--allow-writes`; the hook is case-insensitive and guards its own files; injection and write cases are must-pass. The suite went from 88 to 107 tests. The lesson is the one the workflow is built on: the author's tests encode the author's blind spots, and a reviewer with no stake in the code passing is the cheapest way to find them.

Two findings were not fixed in code because the fix is a dataset change, and the dataset is protected from agent edits: the `save-declined` case would pass a model that falsely claims it saved, and two cases would pass an answer that spells a number out in words. They are listed in the README as open items for a person to decide.

**The code failed its own standard, and was refactored.** When the general `clean-code` skill was added, its size check was run on the existing code and reported an 88-line agent loop, a 51-line parsing method, a 43-line adapter method and a 320-line file. Following `refactor-safely`, the loop was split into a stateless `CopilotAgent`, an `AskSession` holding per-question state and a `ToolExecutor` owning the approval gate; number parsing moved into `NumberToken`. The 107 tests were green before and after, and no assertion was changed; only the two places that construct the agent were updated. The size check now reports nothing and runs in CI.

**Deterministic tools found what AI review had passed.** Switching on SonarAnalyzer, the .NET analyzers and code metrics as build errors produced 12 violations in code that had already been through an AI review: a property with cyclomatic complexity 21, an adapter coupled to 73 types, a parameter named with a reserved word. They were fixed in the code. AI review and static analysis find different things, which is the argument for running both and letting only the deterministic one decide.

**The guard rail held against its own author.** Asked to make the agent's stop hook run the full quality gate, the agent's edit was refused by `protect-paths`, the hook that stops an agent changing its own guard rails. The change was handed to the repository owner instead.

**The AI reviewers were scored.** `benchmarks/reviewer` holds ten code samples with ten planted defects and two clean files. Both reviewers found every defect assigned to them and raised no serious false alarm. `code-reviewer` also reported a length-limit bug in a file planted as clean; it was right, and the sample was fixed. A perfect score mostly shows the benchmark is too easy, and that is recorded in its baseline.

**Mutation testing corrected the picture coverage gave.** Line coverage was 96.8%. The first mutation run killed 55.6% of 604 mutants: many tests execute code without asserting on its result, mostly message text, telemetry tags and report formatting. The first attempt reported 0%, which was a runner mismatch (Stryker's default runner does not drive this test platform) and was treated as a tool failure, not published as a score.

**An independent re-derivation confirmed the pricing library, then found what it would get wrong.** The `quant-reviewer` subagent rebuilt the demo curve from scratch in Python (its own schedule, interpolation and bisection) and matched the library's PV, par rate, DV01 and every bucket to the cent. It then showed that a swap already under way would be mispriced by about 3.5 million per 100 million, and a deposit that had started before the curve date would calibrate to the wrong rate, both without any error. Neither was reachable from the product yet. Both are now refused with an exception, the reviewer's independent figures are pinned in `EngineReferenceTests`, and tolerances it called too loose were tightened from 10% to 0.1%.

**What was not verified.** No Anthropic credentials were available, so the Copilot has never talked to the live model and the eval suite has never run. The adapter's request format is checked against the SDK's own serialisation, not against the API. The three GitHub workflows are syntactically valid and unexecuted. None of the 18 eval cases has a baseline. The review fixes were not themselves re-reviewed by a second independent pass.

## Running things

```bash
dotnet build CurveRisk.slnx
dotnet test
dotnet run --project evals/CurveRisk.Evals -- --trials 3     # needs ANTHROPIC_API_KEY; costs money
```

To try the tools from Claude Code, build once and the `curverisk` MCP server in `.mcp.json` becomes available: ask it to price `T-1001` or run a 50bp shock.
