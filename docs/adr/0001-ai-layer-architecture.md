# ADR 0001: AI layer architecture

Status: accepted, 2026-10-03

## Context

The platform has two uses for language models. Developers use coding agents to build it, and users get a Risk Copilot that answers questions about curves, valuations and risk. Both carry the same hazard in different forms: output that is fluent, plausible and wrong. In a risk system a wrong number that looks right is the worst outcome available, so the design starts from what must be impossible rather than from what the model can do.

The AI layer was built before the analytics engine, so that the constraints on agent-written code were in place before any of that code was written.

## Decisions

### 1. The model never originates a number

Figures reach the user only through `IRiskEngine`. The system prompt says so, but the prompt is guidance; the enforcement is `NumericGrounding`, a deterministic check run on every answer. Each number in the answer must match a numeric value in a successful tool result or a number in the user's own message. Free-text fields in tool results are not numeric evidence, since other people write them. A figure shown with fewer than three significant digits must match exactly; otherwise it may be a rounding. On failure the agent is sent back once with the offending figures; if the next draft still fails, the answer is withheld and the user is told why.

Consequences: the Copilot cannot add two tool results together. That is intended. If users need a combined figure, the engine gets an operation that returns it. The check compares magnitudes, not signs, and has a small set of documented exemptions (counts up to ten, tenors, identifiers, dates); those are listed in the type's documentation and each has a test.

### 2. Writes need a human, enforced in code

Tools are registered with `RequiresApproval`. The agent loop asks `IApprovalGate` before running one, and the default gate denies. Prompt injection is treated as something that will sometimes succeed against the model: a test scripts a model that fully obeys injected text and asserts that nothing is written. The eval suite separately measures how often the real model resists, with the gate deliberately open, so the two layers are assessed independently.

### 3. One tool definition, two surfaces

`ToolCatalog` builds each tool as an `AIFunction` from a C# method: name, description, JSON schema and invoker come from one place. The MCP server registers those functions for external agents (Claude Code, Claude Desktop); the Copilot calls the same functions in process. An MCP server has no way to ask a person for approval, so it offers only read tools unless the operator starts it with `--allow-writes`.

This departs from the plan, which had the Copilot calling the MCP server. An in-process call removes a network hop and a failure mode while keeping the property that mattered, which is that the two surfaces cannot drift apart. MCP remains the integration point for agents outside the process.

### 4. A hand-written agent loop behind a provider port

The loop is about 130 lines in `CopilotAgent` and depends on `IModelClient`, not on the Anthropic SDK. Per-question state lives in `AskSession` and tool execution, with the approval gate, in `ToolExecutor`, so the agent itself is stateless. It is written out because the approval gate, the grounding check and the budget check all sit inside it. The port is what lets the loop, the guardrails and the eval graders be tested with a scripted model: no network, no spend, deterministic.

`AnthropicModelClient` is the only file that references the SDK. It is tested against a stubbed HTTP transport, which verifies the request body and response mapping but not that the live API accepts the request.

Model: `claude-opus-5-5` with effort set explicitly to `medium`. Thinking is left at the model default and thinking blocks are replayed unchanged. The plan's idea of routing easy questions to a smaller model is deferred until eval results exist to justify it; a cheaper model that needs a repair turn is not cheaper.

Not yet done: token streaming to the UI (the port returns whole responses), and server-side refusal fallbacks. A refusal currently ends the turn with a clear status.

### 5. Evals: deterministic graders, expected values from the engine

Cases state the question, the tool calls expected, tools that must not be attempted, and which engine figures the answer must contain. Expected figures are not stored; graders recompute them from the engine at grading time, so cases do not go stale when the engine changes. Every grader has a test showing it fails on the mistake it targets.

No LLM judge yet. A judge needs validating against human labels before its scores mean anything, and nothing here should depend on one for correctness.

The live suite has not been run: no API credentials were available when this layer was built. The pass-rate threshold of 0.9 is therefore a placeholder until there is a baseline. Cases tagged `injection` or `writes` are not averaged: any failed run of one fails the suite.

### 6. Cost and observability

Every model call is costed from its token usage and counted against a process-wide daily cap that is checked before the next call. Traces and metrics follow the OpenTelemetry GenAI conventions (`invoke_agent`, `chat`, `execute_tool` spans; `gen_ai.client.token.usage`). Prompt, answer and tool payload content is not recorded on spans.

Prompt caching: the system prompt and tool list are byte-stable (a test asserts it) and carry a cache breakpoint. The prefix is currently short and may be below the model's minimum cacheable length, in which case nothing is cached; confirm from `cache_read_input_tokens` on the first live run rather than assume.

### 7. Agent harness for development

`CLAUDE.md` states the invariants. Hooks enforce what should not rest on the agent's judgement: reference data, eval cases and generated code cannot be edited; C# is formatted on save; a turn cannot end on a broken build or failing tests. Skills hold repeatable workflows, and subagent briefs define reviewers that see the change without the author's reasoning. The same briefs drive the pull-request review workflow.

## Known gaps

- `FixtureRiskEngine` is a placeholder with one hard-coded curve. The eval dataset has 18 cases against it; the planned 50 need the real engine.
- No rate limiting per user; that belongs to the API host, which does not exist yet.
- The Copilot has no host process. It is a library with tests and an eval runner until the API project is built.
- The GitHub workflows have not been executed.
