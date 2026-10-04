---
name: quant-review
description: Review pricing, curve or risk code for financial-mathematics errors - conventions, units, signs, numerical stability. Use before finishing any change under CurveRisk.Analytics or to an IRiskEngine implementation, and whenever asked to check quant code.
---

# Quant review

Compilers and unit tests catch little of what goes wrong in pricing code. Most defects are a convention applied on one side of a calculation and not the other. Go through the diff once per heading.

## Time

- Day count is explicit at every year-fraction call. ACT/360 for SOFR and USD money market, 30/360 or ACT/ACT for the fixed leg as the trade says. No bare `(end - start).Days / 365.0`.
- Accrual period dates and payment dates are distinct; payment is adjusted, accrual may or may not be.
- Business-day adjustment and calendar are stated, including end-of-month rule and stub position.
- Curve time (ACT/365F from the as-of date, typically) is not confused with accrual time.

## Rates and units

- Each rate carries its compounding (simple, annual, continuous) in its type or name. A zero rate and a par rate are never interchangeable.
- Percent, fraction and basis points are not mixed. Names say which: `ZeroRatePercent`, `ParallelBp`. Check every multiply or divide by 100 or 10,000 against the units on both sides.
- Discount factor on the right curve: discounting curve for PV, projection curve for forwards.

## Signs

- PV is from our side. Pay-fixed gains when rates rise. State the convention once and check each leg against it.
- DV01 convention is written down: PV change for +1bp (not the absolute value, not -1bp).
- Bump direction matches the label.

## Calibration

- The curve reprices every input instrument to tolerance; there is a test that asserts it.
- Solver has an iteration cap, a tolerance on the residual and on the step, and fails loudly with the residual when it does not converge.
- Bracketing methods get a valid bracket or report that none was found.
- Interpolation: which quantity (zero rate, log discount factor), which scheme, what happens beyond the last pillar. Forward rates implied by the scheme are sane.

## Numerics

- No subtraction of nearly equal large numbers where a rearrangement avoids it (`1 - df` for short tenors; use `expm1`/`log1p`).
- Bump size is justified: large enough to clear rounding noise, small enough to stay linear. Central difference where convexity matters.
- Floating-point comparisons use a tolerance with a stated basis.
- AD and bumped sensitivities agree in a test, with a tolerance that reflects the bump's truncation error.

## Evidence

- There is a reference value from an independent source (see `numerical-validation`) for every new instrument or curve feature.
- There is a property test for anything that should hold for all inputs (repricing, monotone discount factors, bucket deltas adding up to the parallel delta).

Report findings as: file and line, what is wrong, the size of the error it causes if you can estimate it, and the fix. Say plainly when you checked a heading and found nothing.
