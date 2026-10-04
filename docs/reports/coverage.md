# Coverage and test report

Snapshot of `node tools/check.mjs` on 2026-10-04: **99.9% of lines** (2288 of 2290) and **98% of branches** (832 of 849; one timing-dependent branch moves this by 0.1 between runs) covered. The gate fails the build below 95% lines, 85% branches, or 85% lines in any one file.

## Tests

| Project | Tests | Failed |
|---|---:|---:|
| `tests/CurveRisk.Ai.Tests` | 380 | 0 |
| `tests/CurveRisk.Analytics.Tests` | 171 | 0 |
| `tests/CurveRisk.Api.Tests` (SQLite locally, PostgreSQL in CI) | 104 | 0 |
| `tests/hooks` (agent guard rails) | 22 | 0 |
| **Total** | **677** | **0** |

## Coverage by file

| File | Line % | Branch % |
|---|---:|---:|
| `evals/CurveRisk.Evals/EvalCase.cs` | 100.0 | 100.0 |
| `evals/CurveRisk.Evals/EvalCli.cs` | 100.0 | 100.0 |
| `evals/CurveRisk.Evals/EvalRunner.cs` | 100.0 | 100.0 |
| `evals/CurveRisk.Evals/EvalSettings.cs` | 100.0 | 77.5 |
| `evals/CurveRisk.Evals/Graders.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Ai.Tools/IRiskEngine.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Ai.Tools/RiskTools.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Ai.Tools/ToolCatalog.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Curves/CurveBootstrapper.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Curves/DiscountCurve.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Curves/Interpolation.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Instruments/CashInstruments.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Instruments/InterestRateSwap.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Numerics/Brent.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Risk/RiskCalculator.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Time/BusinessCalendar.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Time/DayCount.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Time/Schedule.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Time/Tenor.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Analytics/Time/UsHolidays.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/ApiSetup.cs` | 100.0 | 94.4 |
| `src/CurveRisk.Api/Endpoints/ApiEndpoints.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/Persistence/CurveRiskDbContext.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/Services/ApiErrors.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/Services/CopilotService.cs` | 97.8 | 100.0 |
| `src/CurveRisk.Api/Services/MarketSnapshotService.cs` | 100.0 | 96.2 |
| `src/CurveRisk.Api/Services/PricingService.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/Services/RiskRunService.cs` | 98.5 | 91.7 |
| `src/CurveRisk.Api/Services/Timestamps.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Api/Services/TradeService.cs` | 100.0 | 96.2 |
| `src/CurveRisk.Contracts/V1/Contracts.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/AnthropicModelClient.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/AnthropicProviderClient.cs` | 100.0 | 75.0 |
| `src/CurveRisk.Copilot/AnthropicRequestMapper.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/AnthropicResponseMapper.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/Approval.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/AskSession.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/Conversation.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/CopilotAgent.cs` | 100.0 | 93.8 |
| `src/CurveRisk.Copilot/CopilotAnswer.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/CopilotOptions.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/CopilotTelemetry.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/Cost.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/IModelClient.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/NumberToken.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/NumericGrounding.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/ProviderSettings.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Copilot/ToolExecutor.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Engine/AnalyticsRiskEngine.cs` | 100.0 | 100.0 |
| `src/CurveRisk.Engine/MarketSnapshot.cs` | 100.0 | 100.0 |
| **Total** | **99.9** | **98.0** |

The raw Cobertura files are written to `TestResults/` (not committed) and attached to every run of the [ci workflow](https://github.com/paciom/curve_and_risk/actions/workflows/ci.yml) as the `coverage` artifact. To reproduce: `node tools/check.mjs`.
