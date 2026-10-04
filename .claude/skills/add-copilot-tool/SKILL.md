---
name: add-copilot-tool
description: Add or change a tool on the AI surface (the MCP server and the Risk Copilot). Use when asked to expose a new engine capability to the Copilot, add an MCP tool, or change a tool's parameters or description.
---

# Add a Copilot tool

A tool is defined once, in `src/CurveRisk.Ai.Tools`, and appears on both surfaces. Work in this order; each step has a check.

## 1. Port

Add the operation to `IRiskEngine` with typed request and result records. Put units and sign conventions in the XML docs of the record, because the model will read the serialised field names and nothing else. Prefer `ParRatePercent` over `ParRate`.

Invalid input throws `RiskEngineException` with a message the model can act on (say what was wrong and what the valid values are). Anything else is a bug and surfaces as a generic failure.

## 2. Tool method

Add a method to `RiskTools`. The `[Description]` text is prompt text:

- Say when to use it, not only what it does. Compare `run_scenario`: "Use this for any what-if question; never estimate P&L from DV01 yourself."
- State units for every numeric parameter.
- Keep it free of anything that changes between requests. It is part of the cached prompt prefix.

## 3. Catalog

Register it in `ToolCatalog.Create` and add a name constant. Append; do not reorder, the order is part of the cache key.

Does it change stored state, send anything, or cost money? Then `RequiresApproval: true`. If unsure, it does.

## 4. Tests

In `tests/CurveRisk.Ai.Tests`:

- Update the expected list in `Catalog_is_stable_and_only_save_scenario_needs_approval`.
- Add an invocation test through `ToolCatalog.InvokeAsync` with JSON arguments, including one bad-argument case.
- For a write tool, add a denied and an approved case in `CopilotAgentTests`.

## 5. Eval cases

Ask the user to add at least two cases to `evals/datasets/copilot.jsonl`: one where the tool is the right answer and one near-miss where a different tool is. You cannot edit that file (a hook blocks it); propose the JSON lines in your reply instead.

## 6. Verify

```bash
dotnet build CurveRisk.slnx && dotnet test
```

Then check the tool over the wire: start the MCP server and call `tools/list`, confirm the schema has no `cancellationToken` property and that `required` is what you expect.

Finish by asking the `guardrail-reviewer` subagent to review the diff.
