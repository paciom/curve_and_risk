---
name: numerical-validation
description: Validate pricing and curve numbers against an independent reference (QuantLib) and manage golden files. Use when adding an instrument or curve feature, when a golden test fails, or when asked to check that the engine's numbers are right.
---

# Numerical validation

The engine's numbers are trusted because they agree with something that shares no code with it. Golden files under `tests/**/golden/` hold reference values produced by QuantLib-Python; the test suite compares engine output to them.

You cannot edit golden files: a hook blocks it. Regenerating them is a human decision, because a changed reference value means either the engine was wrong before or it is wrong now.

## When a golden test fails

1. Do not touch the reference. Read the diff: which field, how large, what sign.
2. Classify the size. Around 1e-12 relative is floating-point ordering. Around 1e-6 to 1e-4 is usually a convention (day count, compounding, a one-day date shift). Percent-level is a wrong formula or wrong curve.
3. Find the cause in the engine and fix it there.
4. If you conclude the reference itself is wrong, stop and give the user the evidence: the inputs, both numbers, and an independent hand calculation.

## Comparing two result files

```bash
python .claude/skills/numerical-validation/scripts/compare_reference.py actual.json reference.json --rel 1e-10 --abs 1e-8
```

Walks both JSON documents, reports every numeric leaf outside tolerance with its path, and any structural difference. Exit 0 when they agree, 1 when they do not, 2 on bad input. A value passes if it is within either tolerance.

## Adding a reference for a new instrument

1. Write the generator next to the others in `tools/reference/` as a small QuantLib-Python script that takes the same inputs the test uses and writes one JSON file. Pin the QuantLib version in the file header and in `tools/reference/requirements.txt`.
2. Give the script's conventions explicitly (calendar, day counts, compounding, settlement lag). Defaults differ between libraries; that is where disagreements come from.
3. Hand the script to the user to run and commit the output. Say what tolerance you expect the engine to meet and why.
4. Write the test that loads the golden file and compares with that tolerance.

`tools/reference/` does not exist until the first instrument lands (PLAN.md phase 1). Create it then; do not invent reference values in the meantime.

## Tolerances

State the tolerance and its reason in the test. Typical: 1e-10 relative for discount factors from the same inputs and scheme, 1e-8 absolute on par rates, 1e-6 relative on PV where schedules could differ by a convention you have documented. A tolerance widened to make a test pass needs the reason written beside it.
