# Curve & Risk: the financial domain

The software engineering story is in the [README](README.md). This page is about the finance: what the platform models, the conventions it has to get right, and how its numbers are made trustworthy.

> **Status.** The production pricing library is designed ([PLAN.md](PLAN.md)) and not yet built. What exists today is a deliberately simple reference engine ([`FixtureRiskEngine`](src/CurveRisk.Ai.Tools/Fixtures/FixtureRiskEngine.cs)), the tests that pin its financial properties, and the review and validation procedures the real engine will be built under. Each section says which is which.

## What the platform does

Builds interest-rate curves from market quotes, prices rate products off those curves, and reports how their value moves when rates move. Scope is deliberately narrow: USD, SOFR, five linear products. One market done correctly is worth more than five done approximately.

| Capability | Question it answers |
|---|---|
| Curve calibration | What discount factors and forward rates are consistent with today's quotes? |
| Pricing | What is this trade worth, and at what rate would it be worth zero? |
| Risk | How much do I gain or lose per basis point, and where on the curve does it sit? |
| Scenarios | What happens to P&L if the curve shifts, steepens or flattens? |

## Curves

**Why a curve.** A cash flow at time *t* is worth its amount times the discount factor *DF(t)*. Quotes exist only at a handful of maturities, so a curve is the set of discount factors that reprices every quoted instrument, plus a rule for the gaps between them.

**Multi-curve.** Since the 2008 crisis, discounting and projection are separate questions. Collateralised trades discount at the overnight rate (SOFR OIS), while a floating leg's forward rates come from its own index curve. The plan calibrates the discount curve first, then solves the projection curve given it (dual bootstrapping).

**Bootstrapping versus a global solve.** A sequential bootstrap solves one pillar at a time, each with a one-dimensional root find (Brent or Newton). It is fast and exact when each instrument depends only on earlier pillars. Interpolation schemes that look ahead break that assumption, so the plan also includes a global least-squares solve (Levenberg–Marquardt) and compares the two.

**Interpolation is a modelling choice, not a detail.** Every scheme reprices the inputs; they differ in the forward rates they imply between pillars.

| Scheme | Behaviour |
|---|---|
| Log-linear on discount factors | Piecewise-constant forwards. Robust; forwards jump at pillars |
| Linear on zero rates | Simple; implies saw-tooth forwards that can go negative |
| Monotone-convex, monotone cubic | Smooth, positive forwards; risk spreads across neighbouring pillars |

The choice changes where bucketed risk appears, which is why the scheme is pluggable and its effect on forwards is shown, not hidden.

*Today:* the reference engine uses one fixed USD-SOFR zero curve with eight pillars (1Y to 30Y), continuous compounding, linear interpolation on zero rates and flat extrapolation.

## Pricing

A fixed-for-floating swap is two legs:

- **Fixed leg:** notional × fixed rate × the annuity (the sum of accrual fractions times discount factors).
- **Floating leg:** on a single curve with no spread, it is worth notional × (1 − *DF(T)*), because a floating-rate note resets to par.

The **par rate** is the fixed rate that makes the two legs equal: (1 − *DF(T)*) / annuity. A payer swap is in the money when the par rate is above its fixed rate.

Worked from the reference engine: trade `T-1001` pays 3.50% fixed for five years on 100 million. The par rate is 3.8745%, so the payer is worth about +1.67 million: roughly 37 basis points of advantage, times the annuity, times the notional.

*Planned:* deposits, FRAs, OIS, swaps with real schedules (ACT/360 floating, 30/360 or ACT/ACT fixed, business-day adjustment, stubs) and fixed-rate bonds with accrued interest.

## Risk

**DV01.** The change in PV for a one basis point rise in rates. Here the convention is stated in the type, not assumed: PV change for **+1bp**, from **our** side, so a payer swap has a positive DV01.

**Bucketed (key-rate) delta.** The same bump applied to one pillar at a time. It shows where on the curve the risk sits, which a single DV01 cannot. A five-year swap's risk concentrates at the five-year pillar; a hedge that matches total DV01 but not the buckets is exposed to the curve changing shape.

**A consistency property worth testing.** With linear interpolation the pillar bumps partition a parallel shift, so the bucket deltas must add up to the parallel DV01. The suite asserts this ([`ToolLayerTests`](tests/CurveRisk.Ai.Tests/ToolLayerTests.cs)).

**Three ways to compute sensitivities**, all in the plan, to be cross-checked against each other:

| Method | Trade-off |
|---|---|
| Bump and revalue | Simple and model-free; one repricing per bucket; bump size trades truncation error against rounding noise |
| Jacobian transform | Converts zero-rate sensitivities to par-instrument sensitivities, the form a trader hedges with |
| Automatic differentiation (dual numbers) | Exact derivatives in one pass; no bump-size choice |

## Scenarios, and why DV01 is not enough

A scenario reprices the trade on a shocked curve. Two shocks are supported today:

- **Parallel:** every zero rate moves by the same amount.
- **2s10s steepener:** minus half the shock at two years and below, plus half at ten years and above, linear in between.

DV01 times the size of the move is only a first-order estimate. Swap value is convex in rates, so the error grows with the square of the shock. The suite checks that a 10bp scenario agrees with ten times DV01 to within a convexity tolerance, and the product enforces the distinction: the Risk Copilot is not allowed to scale a DV01 into a P&L figure and must call the scenario engine instead. That rule is an [eval case](evals/datasets/copilot.jsonl) (`no-dv01-scaling`).

*Planned:* flattener and butterfly shocks, user-defined shocks, and historical-simulation VaR and expected shortfall from imported rate history.

## Where pricing code goes wrong

A compiler does not catch any of these. They are the checklist in the [`quant-review`](.claude/skills/quant-review/SKILL.md) skill, applied to every change in pricing code.

- **Day count** applied on one side of a calculation and not the other; accrual time confused with curve time.
- **Compounding** mixed: a continuously compounded zero rate used where a simple rate is expected.
- **Units** mixed: percent, fraction and basis points. Field names carry the unit (`ParRatePercent`, `ParallelBp`) so the mistake is visible at the call site.
- **Sign** conventions unstated: whose PV, and DV01 for which direction of bump.
- **Discounting on the wrong curve.**
- **Schedule** errors: a one-day shift from a business-day rule or a stub on the wrong end.
- **Numerical instability:** subtracting nearly equal numbers (`1 − DF` at short tenors), or a bump too small to clear rounding noise.

## How the numbers are made trustworthy

**Independent reference values.** The engine is checked against QuantLib, which shares no code with it. Reference files are generated outside the engine and the coding agent is blocked from editing them: a changed reference means the engine was wrong before or is wrong now, and that is a human decision ([`numerical-validation`](.claude/skills/numerical-validation/SKILL.md)).

**Reading a mismatch by its size.** Around 1e-12 relative is floating-point ordering. Around 1e-6 to 1e-4 is usually a convention. Percent-level is a wrong formula or the wrong curve.

**Properties that must hold for every input**, as tests:

- A swap struck at its par rate has zero PV.
- A calibrated curve reprices its own inputs.
- PV is linear in notional.
- Bucket deltas sum to the parallel delta.
- A zero shock gives zero P&L.
- Discount factors decrease with maturity.

Asserted today against the reference engine: bucket deltas sum to the parallel delta, a zero shock gives zero P&L, a scenario agrees with DV01 to within convexity, and a swap's PV has the sign of par rate minus fixed rate for a payer. The others arrive with the real engine.

**The model never originates a number.** In the Risk Copilot every figure in an answer must trace to an engine result. In a risk system a plausible number that the engine did not produce is worse than no number, because someone will act on it.

## Implemented and planned

| Area | Today | Planned |
|---|---|---|
| Curve | One fixed zero curve, linear interpolation | Calibration from market quotes, multi-curve, three interpolation schemes |
| Products | Vanilla swap, annual fixed coupons, unit year fractions | Deposit, FRA, OIS, IRS with full conventions, bond |
| Risk | Parallel DV01 and bucketed delta by bumping | Jacobian to par risk, automatic differentiation |
| Scenarios | Parallel and 2s10s steepener | Flattener, butterfly, custom shocks, historical VaR and ES |
| Validation | Property tests on the reference engine | QuantLib golden values for every instrument |
| Numerical methods | None needed | Brent, Newton, Levenberg–Marquardt, dual-number AD |

The reference engine is not market-accurate and is not meant to be. It exists so the layers above it could be built and tested first, and it is replaced when the real library lands.
