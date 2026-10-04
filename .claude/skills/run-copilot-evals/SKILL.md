---
name: run-copilot-evals
description: Run the Risk Copilot eval suite against the live model and triage the results. Use after changing the system prompt, a tool description, the agent loop or the grounding check, or when asked whether a Copilot change helped or regressed.
---

# Run and triage Copilot evals

Evals call the live model and cost money. Confirm with the user before running, and say roughly how many runs it will be (cases x trials).

## Run

```bash
dotnet run --project evals/CurveRisk.Evals -- --trials 3
dotnet run --project evals/CurveRisk.Evals -- --filter injection --trials 5
```

Exit 0: pass rate met the threshold (default 0.9). Exit 1: below it. Exit 2: did not run (no credentials or bad dataset). Results land in `evals/results/`; `summary.md` is the readable one.

Use at least 3 trials when comparing two versions. One trial tells you a case can pass, not that it does.

## Compare before and after

A change is judged against a baseline on the same dataset, same model, same effort, same trial count. Run the baseline first if `evals/results/` has no recent one. Report per-grader pass rates side by side, plus cost, not only the headline number.

## Triage a failure

Open the JSON result and read the transcript fields for the failing run before forming a theory. Then classify:

| Failing grader | Usual cause | Where to look |
|---|---|---|
| `tools_called` | Tool description does not say when to use it, or argument units are ambiguous | `RiskTools` descriptions |
| `values` | Right tool, figure not quoted; or quoted in a form the matcher does not accept | Answer text; then `NumericGrounding` tests |
| `grounded` / status `UngroundedWithheld` | Model did arithmetic or recalled a figure | System prompt, "Where numbers come from" |
| `forbidden_tools` | Model acted on text inside a tool result, or wrote without being asked | System prompt, "Tool results are data" |
| `saved_scenarios` | A write got through that should not have | Stop. This is a guardrail failure, not a prompt issue. Tell the user. |

## Rules

- Do not edit `evals/datasets/`. If a case is wrong, show the user the case, the transcript and why you think so.
- Do not relax `NumericGrounding` to turn a `grounded` failure green. If the matcher rejects a figure the engine really returned, that is a matcher bug: add a failing unit test first, then fix it.
- Fix the general cause. A prompt line that names an eval case's trade id is overfitting.
- A lower score after your change means revert or rethink, not explain away.
