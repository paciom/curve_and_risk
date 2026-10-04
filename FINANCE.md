# Curve & Risk: the financial domain

The software engineering story is in the [README](README.md). This page is about the finance: what the platform models, the conventions it has to get right, and how its numbers are made trustworthy.

> **Status.** The pricing library ([`CurveRisk.Analytics`](src/CurveRisk.Analytics/)) is implemented for a single-curve USD SOFR world and is the engine behind every number the product shows. It is validated by closed-form and property tests; validation against QuantLib, a separate projection curve and the items marked *planned* below are not done yet.

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

**Multi-curve.** Since the 2008 crisis, discounting and projection are separate questions. Collateralised trades discount at the overnight rate (SOFR OIS), while a floating leg's forward rates come from its own index curve. For SOFR swaps the two coincide, which is the case implemented: one curve both discounts and projects. A separate projection curve solved against a fixed discount curve (dual bootstrapping) is *planned*.

**Bootstrapping.** A sequential bootstrap solves one pillar at a time, each with a one-dimensional root find; here that is [Brent's method](src/CurveRisk.Analytics/Numerics/Brent.cs), which needs a bracket and no derivative. It is exact in one pass when each instrument depends only on pillars up to its own. An interpolation scheme that looks ahead breaks that assumption, so the [bootstrapper](src/CurveRisk.Analytics/Curves/CurveBootstrapper.cs) repeats the pass until every quote reprices to 1e-12: one sweep for the local schemes, several for the cubic. A global least-squares solve (Levenberg–Marquardt) is *planned* as a cross-check.

**Interpolation is a modelling choice, not a detail.** Every scheme reprices the inputs; they differ in the forward rates they imply between pillars.

| Scheme | Behaviour |
|---|---|
| Log-linear on discount factors | Piecewise-constant forwards. Robust; forwards jump at pillars |
| Linear on zero rates | Simple; implies saw-tooth forwards that can go negative |
| Monotone cubic on log discount factors | Smooth forwards with no overshoot; risk spreads across neighbouring pillars |

The choice changes where bucketed risk appears, which is why the scheme is pluggable and its effect on forwards is shown, not hidden.

All three are [implemented](src/CurveRisk.Analytics/Curves/Interpolation.cs) and selectable per curve. Time on the curve is ACT/365F from the as-of date, zero rates are continuously compounded, and the zero rate is held flat beyond the last pillar.

## Pricing

A fixed-for-floating swap is two legs:

- **Fixed leg:** notional × fixed rate × the annuity (the sum of accrual fractions times discount factors).
- **Floating leg:** on a single curve with no spread, it is worth notional × (1 − *DF(T)*), because a floating-rate note resets to par.

The **par rate** is the fixed rate that makes the two legs equal: (1 − *DF(T)*) / annuity. A payer swap is in the money when the par rate is above its fixed rate.

Worked example from the demo market: trade `T-1001` pays 3.50% fixed for five years on 100 million. The curve is calibrated to a 5Y par quote of 3.78%, and the swap's par rate comes back as exactly 3.78%, which is calibration doing its job. The payer is worth +1,270,062: 28 basis points of advantage, times an annuity of about 4.5, times the notional.

Swaps follow [USD SOFR OIS conventions](src/CurveRisk.Analytics/Instruments/InterestRateSwap.cs): spot start two business days after trade date, annual fixed payments, ACT/360, modified following, US holiday calendar, schedules generated backward from maturity so any stub is at the front. [Deposits, forward rate agreements and fixed-rate bonds](src/CurveRisk.Analytics/Instruments/CashInstruments.cs) (with accrued interest and clean price) are implemented alongside.

*Planned:* payment lag, compounding-in-arrears detail on the floating leg, and yield-to-maturity for bonds.

## Risk

**DV01.** The change in PV for a one basis point rise in rates. Here the convention is stated in the type, not assumed: PV change for **+1bp**, from **our** side, so a payer swap has a positive DV01.

**Bucketed (key-rate) delta.** The same bump applied to one pillar at a time. It shows where on the curve the risk sits, which a single DV01 cannot. A five-year swap's risk concentrates at the five-year pillar; a hedge that matches total DV01 but not the buckets is exposed to the curve changing shape.

**A consistency property worth testing.** With linear interpolation the pillar bumps partition a parallel shift, so the bucket deltas must add up to the parallel DV01. The suite asserts this ([`ToolLayerTests`](tests/CurveRisk.Ai.Tests/ToolLayerTests.cs)).

**Zero risk and par risk.** Both are [implemented](src/CurveRisk.Analytics/Risk/RiskCalculator.cs) by bump and revalue.

| Measure | How | What it tells you |
|---|---|---|
| Zero-rate delta | Bump one pillar's zero rate, reprice | Where on the curve the exposure sits |
| Par delta | Bump one market quote, recalibrate the whole curve, reprice | Exposure in terms of the instruments you would hedge with |

The difference shows on `T-1001`. Its zero-rate risk is 43,710 at the five-year pillar with small amounts at one, two and three years, where the coupons fall. In par terms a swap struck at the market rate shows its entire risk in its own maturity bucket and nothing elsewhere, which the tests assert. Automatic differentiation with dual numbers is *planned* as a third method to cross-check both.

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

All six are asserted in the [test suite](tests/CurveRisk.Analytics.Tests/), for each interpolation scheme where the property applies.

**Closed-form checks.** On a flat curve every discount factor is exp(−rt), so a swap, a forward rate agreement and a bond can each be valued by hand and compared to fourteen decimal places. A single deposit must calibrate to 1 / (1 + rτ).

**The model never originates a number.** In the Risk Copilot every figure in an answer must trace to an engine result. In a risk system a plausible number that the engine did not produce is worse than no number, because someone will act on it.

## Implemented and planned

| Area | Implemented | Planned |
|---|---|---|
| Conventions | ACT/360, ACT/365F, 30/360; US holiday calendar; business-day adjustment; backward schedule generation with stubs | Good Friday and ad hoc closures; end-of-February 30/360 rule |
| Curve | Iterative bootstrap from deposits and OIS par quotes; three interpolation schemes | Separate projection curve; global least-squares solve |
| Products | OIS and fixed-float swaps, deposits, forward rate agreements, fixed-rate bonds | Payment lag; compounding in arrears; bond yield |
| Risk | Parallel DV01, zero-rate bucketed delta, par delta by recalibration | Automatic differentiation |
| Scenarios | Parallel and 2s10s steepener | Flattener, butterfly, custom shocks, historical VaR and expected shortfall |
| Validation | Closed forms and property tests | QuantLib reference values for every instrument |
| Market data | An illustrative demo snapshot | Imported history from public sources |

The demo market is plausible, not sourced from a vendor. No number here should be read as a real market level.
