---
name: debug-systematically
description: Find the root cause of a bug, failing test, crash or unexpected behaviour by reproducing it, forming and testing hypotheses, and fixing the cause rather than the symptom. Use when something fails or behaves unexpectedly, when a test is flaky, or when a fix attempt has not worked.
---

# Debug systematically

Guessing produces fixes that hide a symptom and leave the cause in place. The method below is slower for the first ten minutes and faster for everything after.

## 1. Reproduce

Get a reliable, minimal way to make it fail before changing anything. Read the actual error text and the whole stack trace, not the first line. If it cannot be reproduced, gather evidence (logs, inputs, timing, environment) until it can; do not start "fixing".

## 2. Locate

Narrow where it goes wrong before asking why.

- What changed? Check recent commits, dependency updates, configuration and data.
- Bisect: over history (`git bisect`), over the input (halve it), over the code path (check the value at the midpoint).
- Find the first point where actual differs from expected. The crash site is usually downstream of the fault.

## 3. Hypothesise and test

State one hypothesis that explains *all* the evidence, including the cases that work. Then design the cheapest observation that would prove it wrong, and run it. One variable at a time. Write down what you ruled out so you do not circle back.

If a hypothesis fails, do not stack a second change on top of the first. Undo, and think again.

## 4. Fix the cause

- Ask why the fault was possible, not only where it surfaced. A null check at the crash site may hide a value that should never have been null.
- Write a test that fails for this bug, then fix, then see it pass.
- Look for the same mistake elsewhere: the same pattern, the same author assumption, the same helper.

## 5. Verify and report

Run the original reproduction and the full suite. Report the cause, the evidence that it was the cause, the fix, and anything still unexplained. "It works now" without knowing why is not a fix; say so if that is where you are.

## Traps

- Changing several things at once, then not knowing which mattered.
- Fixing the first anomaly you see instead of the one that explains the failure.
- Trusting a comment, a name or your memory over what the code and the debugger show.
- Retrying a flaky test until it passes. Flakiness is a bug: shared state, timing, order dependence or an unmocked clock.
- Suppressing the error (catch, default value, disabled test) and calling it fixed.
- After three failed attempts, continuing to patch. Stop and question the assumption all three shared.
