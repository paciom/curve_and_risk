---
name: add-instrument
description: Add a financial instrument (deposit, FRA, OIS, swap, bond, ...) to the analytics library with pricing, calibration support, risk and independent validation. Use when asked to support a new product type or to make an existing product usable as a curve calibration input.
---

# Add an instrument

Applies to `src/CurveRisk.Analytics` (netstandard2.0, no I/O, no DI), which is created in PLAN.md phase 1. If it does not exist yet, set it up per the plan first and record the structure in an ADR; this skill assumes the layout below and should be corrected if the ADR differs.

## Definition of done

An instrument is finished when all of these exist. Do them in this order, because each one is the check for the one before.

1. **Reference values first.** A QuantLib script and golden file for two or three concrete trades, including one with a stub or an awkward date. Follow `numerical-validation`. Writing the expected numbers before the code keeps the code from defining its own correctness.
2. **Immutable definition type** in `Instruments/`: every convention is a field (day count, calendar, business-day rule, payment lag, compounding). No defaults that hide a market convention; use named factory methods like `UsdSofrOis(...)` for the standard cases.
3. **Pricer** in `Pricing/`: a pure function of the instrument and a curve set. Returns PV in the trade currency from our side, plus the par rate or equivalent quote. No static state.
4. **Calibration helper** if it can be a curve input: the residual the solver drives to zero is `modelQuote - marketQuote` in quote units.
5. **Tests**
   - Golden: agrees with QuantLib to a stated tolerance.
   - Property (FsCheck): a trade struck at the par rate has zero PV; a curve calibrated to this instrument reprices it; PV is linear in notional.
   - Risk: bucketed deltas sum to the parallel delta; AD and bump agree.
6. **Port and tool** if users should reach it through the Copilot: extend the `IRiskEngine` implementation, then follow `add-copilot-tool`.
7. **Review**: run the `quant-review` skill on your own diff, then ask the `quant-reviewer` subagent with only the diff and the instrument's term sheet conventions.

## netstandard2.0 constraints

No `Span<T>`-only APIs, `DateOnly`, default interface methods, or `Math.FusedMultiplyAdd`. Records and `init` need the `IsExternalInit` polyfill already in the project. The test project multi-targets so these are caught at build time.

## Conventions worth getting right the first time

- Dates are `DateTime` with `Kind.Unspecified` at midnight, wrapped in the library's `Date` type. Never `DateTime.Now`: the valuation date is always an input.
- Rates are fractions internally (`0.0385`), percent only at the API and tool boundary, where the name says so.
- Schedules are generated backward from maturity unless the instrument says otherwise.
