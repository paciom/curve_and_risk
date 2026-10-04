# Mutation report

Stryker.NET, full run on 2026-10-04: **92.04%** of 1,483 tested mutants detected (1,357 killed, 8 timed out, 117 survived, 1 not covered). Of the 118 undetected, 87 are `ConfigureAwait(false)` flipped to `true`, which no test can observe.

This is a snapshot of the report Stryker writes to `StrykerOutput/<run>/reports/` (not committed). To get the interactive version, with every mutant shown in the source, run `dotnet stryker` and open `mutation-report.html`, or download the `mutation-report` artifact from a run of the [mutation workflow](https://github.com/paciom/curve_and_risk/actions/workflows/mutation.yml).

![Stryker.NET report: 1,365 mutants detected, 117 survived](mutation-report.png)

## By file

| File                               | Score   | Killed | Survived | Timeout | No Coverage | Ignored | Compile Errors | Runtime Errors | Total Detected | Total Undetected | Total Mutants |
| ---------------------------------- | ------- | ------ | -------- | ------- | ----------- | ------- | -------------- | -------------- | -------------- | ---------------- | ------------- |
| ApiSetup.cs                        | 95.65%  | 44     | 2        | 0       | 0           | 9       | 2              | 0              | 44             | 2                | 57            |
| Endpoints/ApiEndpoints.cs         | 69.49%  | 41     | 18       | 0       | 0           | 10      | 0              | 0              | 41             | 18               | 69            |
| Persistence/CurveRiskDbContext.cs | 91.30%  | 21     | 2        | 0       | 0           | 4       | 0              | 0              | 21             | 2                | 27            |
| Program.cs                         | n/a    | 0      | 0        | 0       | 0           | 4       | 0              | 0              | 0              | 0                | 4             |
| Services/ApiErrors.cs             | 93.75%  | 15     | 1        | 0       | 0           | 2       | 5              | 0              | 15             | 1                | 23            |
| Services/CopilotService.cs        | 85.71%  | 18     | 3        | 0       | 0           | 5       | 2              | 0              | 18             | 3                | 28            |
| Services/MarketSnapshotService.cs | 88.00%  | 43     | 6        | 1       | 0           | 12      | 8              | 0              | 44             | 6                | 70            |
| Services/PricingService.cs        | 9.09%   | 1      | 10       | 0       | 0           | 6       | 1              | 0              | 1              | 10               | 18            |
| Services/RiskRunService.cs        | 65.12%  | 28     | 15       | 0       | 0           | 8       | 5              | 0              | 28             | 15               | 56            |
| Services/Timestamps.cs            | 100.00% | 2      | 0        | 0       | 0           | 1       | 0              | 0              | 2              | 0                | 3             |
| Services/TradeService.cs          | 84.55%  | 93     | 17       | 0       | 0           | 23      | 7              | 0              | 93             | 17               | 140           |
| AnthropicModelClient.cs            | 85.71%  | 12     | 2        | 0       | 0           | 7       | 0              | 0              | 12             | 2                | 21            |
| AnthropicProviderClient.cs         | 83.33%  | 5      | 1        | 0       | 0           | 1       | 0              | 0              | 5              | 1                | 7             |
| AnthropicRequestMapper.cs          | 100.00% | 14     | 0        | 0       | 0           | 3       | 13             | 0              | 14             | 0                | 30            |
| AnthropicResponseMapper.cs         | 100.00% | 23     | 0        | 0       | 0           | 2       | 0              | 0              | 23             | 0                | 25            |
| Approval.cs                        | 100.00% | 4      | 0        | 0       | 0           | 0       | 0              | 0              | 4              | 0                | 4             |
| AskSession.cs                      | 100.00% | 31     | 0        | 0       | 0           | 10      | 4              | 0              | 31             | 0                | 45            |
| Conversation.cs                    | 100.00% | 5      | 0        | 0       | 0           | 0       | 0              | 0              | 5              | 0                | 5             |
| CopilotAgent.cs                    | 82.35%  | 28     | 5        | 0       | 1           | 10      | 2              | 0              | 28             | 6                | 46            |
| CopilotAnswer.cs                   | 100.00% | 7      | 0        | 0       | 0           | 0       | 0              | 0              | 7              | 0                | 7             |
| CopilotOptions.cs                  | 100.00% | 3      | 0        | 0       | 0           | 0       | 0              | 0              | 3              | 0                | 3             |
| CopilotTelemetry.cs                | 100.00% | 64     | 0        | 0       | 0           | 4       | 0              | 0              | 64             | 0                | 68            |
| Cost.cs                            | 100.00% | 16     | 0        | 0       | 0           | 5       | 0              | 0              | 16             | 0                | 21            |
| IModelClient.cs                    | n/a    | 0      | 0        | 0       | 0           | 0       | 0              | 0              | 0              | 0                | 0             |
| NumberToken.cs                     | 100.00% | 91     | 0        | 2       | 0           | 4       | 5              | 0              | 93             | 0                | 102           |
| NumericGrounding.cs                | 100.00% | 43     | 0        | 0       | 0           | 15      | 2              | 0              | 43             | 0                | 60            |
| ProviderSettings.cs                | 100.00% | 33     | 0        | 0       | 0           | 5       | 5              | 0              | 33             | 0                | 43            |
| ToolExecutor.cs                    | 73.68%  | 14     | 5        | 0       | 0           | 10      | 3              | 0              | 14             | 5                | 32            |
| V1/Contracts.cs                   | n/a    | 0      | 0        | 0       | 0           | 0       | 0              | 0              | 0              | 0                | 0             |
| EvalCase.cs                        | 100.00% | 21     | 0        | 0       | 0           | 5       | 1              | 0              | 21             | 0                | 27            |
| EvalCli.cs                         | 75.00%  | 33     | 11       | 0       | 0           | 5       | 12             | 0              | 33             | 11               | 61            |
| EvalRunner.cs                      | 92.68%  | 38     | 3        | 0       | 0           | 8       | 16             | 0              | 38             | 3                | 65            |
| EvalSettings.cs                    | 100.00% | 20     | 0        | 0       | 0           | 3       | 4              | 0              | 20             | 0                | 27            |
| Graders.cs                         | 96.92%  | 63     | 2        | 0       | 0           | 13      | 21             | 0              | 63             | 2                | 99            |
| Program.cs                         | n/a    | 0      | 0        | 0       | 0           | 5       | 4              | 0              | 0              | 0                | 9             |
| Curves/CurveBootstrapper.cs       | 100.00% | 23     | 0        | 0       | 0           | 10      | 7              | 0              | 23             | 0                | 40            |
| Curves/DiscountCurve.cs           | 98.15%  | 53     | 1        | 0       | 0           | 10      | 5              | 0              | 53             | 1                | 69            |
| Curves/Interpolation.cs           | 95.37%  | 103    | 5        | 0       | 0           | 11      | 0              | 0              | 103            | 5                | 119           |
| Instruments/CashInstruments.cs    | 100.00% | 34     | 0        | 0       | 0           | 3       | 2              | 0              | 34             | 0                | 39            |
| Instruments/Contracts.cs          | n/a    | 0      | 0        | 0       | 0           | 0       | 0              | 0              | 0              | 0                | 0             |
| Instruments/InterestRateSwap.cs   | 100.00% | 19     | 0        | 0       | 0           | 4       | 2              | 0              | 19             | 0                | 25            |
| Numerics/Brent.cs                 | 95.10%  | 97     | 5        | 0       | 0           | 8       | 0              | 0              | 97             | 5                | 110           |
| Risk/RiskCalculator.cs            | 100.00% | 13     | 0        | 0       | 0           | 3       | 0              | 0              | 13             | 0                | 16            |
| Time/BusinessCalendar.cs          | 100.00% | 21     | 0        | 1       | 0           | 3       | 0              | 0              | 22             | 0                | 25            |
| Time/DayCount.cs                  | 100.00% | 17     | 0        | 0       | 0           | 1       | 2              | 0              | 17             | 0                | 20            |
| Time/Schedule.cs                  | 95.83%  | 21     | 1        | 2       | 0           | 5       | 1              | 0              | 23             | 1                | 30            |
| Time/Tenor.cs                     | 100.00% | 3      | 0        | 0       | 0           | 0       | 15             | 0              | 3              | 0                | 18            |
| Time/UsHolidays.cs                | 96.00%  | 22     | 1        | 2       | 0           | 3       | 0              | 0              | 24             | 1                | 28            |
| AnalyticsRiskEngine.cs             | 100.00% | 41     | 0        | 0       | 0           | 9       | 5              | 0              | 41             | 0                | 55            |
| MarketSnapshot.cs                  | 100.00% | 10     | 0        | 0       | 0           | 0       | 0              | 0              | 10             | 0                | 10            |
| IRiskEngine.cs                     | n/a    | 0      | 0        | 0       | 0           | 0       | 0              | 0              | 0              | 0                | 0             |
| RiskTools.cs                       | n/a    | 0      | 0        | 0       | 0           | 0       | 0              | 0              | 0              | 0                | 0             |
| ToolCatalog.cs                     | 85.71%  | 6      | 1        | 0       | 0           | 3       | 0              | 0              | 6              | 1                | 10            |

## The final mutation score is 92.04%

### *Coverage Thresholds: high:85 low:70 break:0*
