# Curve & Risk — Project Plan

A small but production-shaped interest-rate curve calibration and risk platform.
It has three goals:

1. **Software engineering** — a modern .NET stack (.NET 10, ASP.NET Core, PostgreSQL, Aspire, OpenTelemetry, .NET Standard 2.0).
2. **Quant correctness** — curve bootstrapping, pricing, risk, scenarios, with numerics validated against an independent reference.
3. **AI engineering** — agents constrained by harnesses, verified by evals, and observable in traces.

## 1. Principle

A model's output is a proposal. What makes it usable is the **system around the model**: context, tools, guardrails,
verification and measurement. Every AI element in this repo therefore ships with evidence that it works:

| Principle | Evidence in the repo |
|---|---|
| Agents are directed with engineered context | `CLAUDE.md`, skills, subagents, ADRs |
| Agent output is not trusted blindly | Hooks that build/format/test on edit; golden numerical tests the agent may not edit; CI gates |
| AI features are more than chat wrappers | Risk Copilot: tool-using agent over typed tools, numbers only ever come from the engine |
| AI quality is measured | Eval suite with deterministic graders run in CI; regression thresholds |
| AI is operated, not just shipped | OpenTelemetry GenAI spans, token/cost metrics, rate limits, budget cap, approval gates on write tools |

## 2. Domain scope (kept deliberately narrow and deep)

**Market:** USD rates, SOFR. One currency done properly beats five done shallowly.

- **Conventions:** day counts (ACT/360, ACT/365F, 30/360), business-day adjustment, calendars, schedule generation.
- **Curve calibration:** SOFR OIS discount curve from deposits / futures / OIS swaps; a second projection curve to demonstrate
  multi-curve dual bootstrapping. Sequential bootstrap **and** global solve (Levenberg–Marquardt) with comparison.
- **Interpolation:** log-linear on discount factors, linear zero, monotone-convex / monotone cubic — pluggable via an interface,
  with a page showing the effect on forward curves.
- **Products:** deposit, FRA, OIS, fixed-float IRS, fixed-rate bond. Par rate, PV, accrued.
- **Risk:** PV01/DV01, bucketed (key-rate) delta by bump-and-reval, and by **Jacobian transform** (zero-rate → par-instrument
  sensitivities). Forward-mode **automatic differentiation with dual numbers** as the numerical-methods showpiece, cross-checked against bumping.
- **Scenarios:** parallel / steepener / flattener / butterfly shocks, user-defined shocks, historical-simulation VaR and
  expected shortfall from imported history.
- **Numerical methods on show:** Brent and Newton root finding, LM least squares, interpolation schemes, AD, numerical stability
  and tolerance handling, performance benchmarks (BenchmarkDotNet).
- **Stretch:** Hull–White one-factor Monte Carlo for a swaption, with variance reduction and convergence plots.

## 3. Architecture

```
src/
  CurveRisk.Analytics/        netstandard2.0 — pure quant library, no I/O, no DI; shared with Excel and other .NET Framework hosts
  CurveRisk.Contracts/        netstandard2.0 — typed API DTOs, shared with clients
  CurveRisk.Application/      net10.0 — use cases, orchestration, ports
  CurveRisk.Infrastructure/   net10.0 — EF Core + Npgsql, reference-data and external-source adapters
  CurveRisk.Api/              net10.0 — versioned ASP.NET Core REST API, OpenAPI
  CurveRisk.Worker/           net10.0 — BackgroundService: market-data imports, async risk runs
  CurveRisk.Mcp/              net10.0 — MCP server exposing the engines as typed tools
  CurveRisk.Copilot/          net10.0 — agent service (Claude API, tool loop, streaming)
  CurveRisk.AppHost/          .NET Aspire orchestration (Postgres, API, worker, MCP, copilot, web)
  CurveRisk.ServiceDefaults/  OpenTelemetry, health checks, resilience
web/                          React + TypeScript, client generated from OpenAPI
clients/python/               generated client + a notebook reproducing a curve build
clients/excel/                Excel-DNA add-in over CurveRisk.Analytics (proves netstandard2.0 compatibility for real)
tests/                        see section 5
evals/                        Copilot eval datasets and graders
docs/adr/                     architecture decision records
.claude/                      skills, agents, hooks, settings, commands
```

**API design points to demonstrate**

- URL-versioned (`/api/v1`, `/api/v2`) with `Asp.Versioning`; v2 exists for a real reason (e.g. multi-curve response shape) with v1 kept working.
- Persistent resources: `market-snapshots`, `curve-definitions`, `curves` (immutable calibrated results), `portfolios`, `trades`, `scenarios`, `risk-runs`.
- Long-running risk runs as a job resource: `POST` → `202 Accepted` + `Location`, polling, cancellation.
- Idempotency keys on POST, ETags / optimistic concurrency, cursor pagination, RFC 9457 problem details, strongly typed IDs.
- `async` end to end with `CancellationToken`; `Channel<T>`-based job queue; CPU-bound calibration kept off request threads.
- Bitemporal-lite market data: `as_of` and `recorded_at`, so any past risk number is reproducible.

**Data:** PostgreSQL via EF Core migrations; `jsonb` for curve pillars; external sources are free and public
(US Treasury par yields, NY Fed SOFR, FRED) behind an `IMarketDataSource` port with Polly resilience and a recorded-fixture fallback.

**Observability:** Aspire dashboard; custom `ActivitySource` spans around calibration (iterations, residual norm as tags);
metrics for calibration time, solver iterations, risk-run duration, import lag; structured logs with trace correlation.

## 4. The AI layer

### 4a. Agentic engineering harness (how the code was built)

- **`CLAUDE.md`** (root + per project): build/test commands, architecture rules, numerical tolerances, "never edit golden files".
- **Skills** in `.claude/skills/`, each a real repeatable workflow with scripts, not a paragraph of prose:
  - `add-instrument` — scaffold product, pricer, calibration helper, tests, reference fixture.
  - `add-endpoint` — contract first, then handler, then contract test and OpenAPI snapshot update.
  - `numerical-validation` — regenerate QuantLib reference values and diff within tolerance.
  - `ef-migration` — create, review SQL, test against Testcontainers.
  - `quant-review` — checklist review of day-count, compounding, sign and unit conventions.
- **Subagents** in `.claude/agents/`: `quant-reviewer`, `api-contract-guardian`, `test-author` — narrow tools, independent of the author's context.
- **Hooks:** post-edit `dotnet format` + build; pre-edit block on `tests/**/golden/**` and generated clients; stop hook runs the fast test suite.
- **Permissions** in `.claude/settings.json`: allowlisted commands, denied destructive ones.
- **MCP** in `.mcp.json`: Postgres (read-only), Aspire, and the project's own `CurveRisk.Mcp` — the agent debugs the app through its own telemetry.
- **CI:** Claude Code GitHub Action for PR review with the same subagents; it comments, humans merge.
- **`docs/ai-workflow.md`:** the spec → plan → implement → verify loop, with two or three honest case studies including one
  where the agent was wrong and the harness caught it.

### 4b. Risk Copilot (AI as a product feature)

A chat panel where a user asks "why did my DV01 move?" or "what happens under a 50bp bear steepener?"

- **Tool use over the MCP server:** `get_curve`, `price_trade`, `run_risk`, `run_scenario`, `explain_calibration`.
- **Hard rule: the model never produces a number.** Every figure in an answer is cited to a tool result; a post-check rejects uncited numbers.
- Structured outputs for charts/tables; streaming over SSE; prompt caching on the system prompt and tool definitions.
- Read tools run freely; write tools (save scenario, create trade) require explicit user approval in the UI.
- Imported market data and trade descriptions are treated as untrusted input (prompt-injection test cases included).
- Model routing: a small model for intent/routing, a larger one for analysis; choice justified by eval results and cost.

### 4c. Evals and AI observability

- `evals/` dataset of ~50 questions with known answers computed by the engine.
- **Deterministic graders first** (right tool called, right arguments, numbers match engine output); LLM-as-judge only for explanation quality.
- Runs in CI on prompt/tool changes; fails the build below a threshold; results published on the site.
- OpenTelemetry GenAI semantic-convention spans: one trace shows HTTP request → agent turn → tool call → calibration → SQL.
- Token, cost and latency metrics; per-IP rate limit and a daily budget cap; recorded-replay mode when the cap is hit so the public demo never breaks.

## 5. Testing strategy

| Layer | Tooling | What it proves |
|---|---|---|
| Unit | xUnit, FluentAssertions-style asserts | Day counts, schedules, interpolators, solvers |
| Property-based | FsCheck | Curve reprices its own inputs; DF monotonicity; AD delta ≈ bump delta |
| Golden / reference | QuantLib-Python generated fixtures | Numbers agree with an independent library to stated tolerance |
| Integration | Testcontainers PostgreSQL, `WebApplicationFactory` | Real database, real migrations, real HTTP pipeline |
| API contract | Verify snapshots of OpenAPI; v1 compatibility tests | Breaking changes are caught, not discovered |
| Distributed | `Aspire.Hosting.Testing` | Whole app graph boots and passes a smoke scenario |
| Front end | Vitest, Playwright | Typed client and key user journeys |
| AI | `evals/` | Copilot correctness and regression |
| Other | BenchmarkDotNet, Stryker mutation testing on Analytics, multi-target test run for netstandard2.0 | Performance and test quality |

## 6. The website

Public, deployed, and doubling as the guided tour of the repo:

1. **Curve Builder** — edit quotes, pick interpolation, see zero / forward / discount curves and calibration residuals live.
2. **Pricing & Risk** — portfolio PV, bucketed delta ladder, bump vs AD vs Jacobian side by side.
3. **Scenarios & VaR** — shock designer, P&L distribution.
4. **Copilot** — with a visible "show tool calls" trace panel.
5. **How it's built** — architecture diagram, live test and eval reports, ADRs, the AI workflow write-up, links to source for each feature.

**Deployment:** Docker images; `azd` from the Aspire AppHost to Azure Container Apps + Azure Database for PostgreSQL;
GitHub Actions for build → test → evals → deploy. Covers the Docker/Azure/CI-CD "beneficial" line.

## 7. Delivery phases

Each phase ends in something demonstrable, so the project is usable at every stage.

| Phase | Deliverable | Rough effort |
|---|---|---|
| 0. Foundations | `git init`, solution skeleton, Aspire AppHost, CI, `CLAUDE.md`, hooks, first skills, ADR-001 | 1–2 days |
| 1. Analytics core | Conventions, single-curve bootstrap, deposit/OIS/IRS pricing, QuantLib golden tests | 3–4 days |
| 2. API + persistence | v1 API, PostgreSQL, snapshots/curves/trades, integration + contract tests | 3–4 days |
| 3. Risk + scenarios | Bump risk, AD, Jacobian, scenarios, async risk-run resource, worker | 3–4 days |
| 4. Web UI | Curve Builder and Risk pages, generated TS client | 3 days |
| 5. Imports + observability | External sources, background import, custom traces/metrics | 2 days |
| 6. MCP + Copilot + evals | MCP server, agent, eval suite in CI, GenAI tracing | 4–5 days |
| 7. Deploy + polish | Azure, "How it's built" page, README, ai-workflow case studies | 2–3 days |
| Stretch | Multi-curve v2 API, VaR, Excel add-in, Python notebook, Hull–White MC | as time allows |

**Minimum credible version** if time is short: phases 0–3 plus a thin slice of 6 (MCP server + 15 evals) and the README.
The UI is the most cuttable part; the tests, harness and evals are the least.

## Status against the phases

| Phase | State |
|---|---|
| 0. Foundations | Done |
| 1. Analytics core | Done for a single-curve SOFR world. Not done: QuantLib reference values, projection curve |
| 2. API + persistence | Done: v1 API, EF Core, SQLite locally and PostgreSQL by configuration. Not done: migrations, OpenAPI snapshot acceptance |
| 3. Risk + scenarios | Done: zero and par risk, parallel and steepener shocks, background risk runs. Not done: automatic differentiation, VaR |
| 4. Web UI | A single page served by the API (plain JavaScript and SVG). The planned React and TypeScript client is not built |
| 5. Imports + observability | Copilot telemetry only. Not done: market-data imports, Aspire, API tracing |
| 6. MCP + Copilot + evals | Done, and exposed through the API. Not run against a live model |
| 7. Deploy | Not done |

## 8. Risks and how the plan handles them

- **Scope creep** — one currency, five products, phases with hard exits.
- **Agent-written code nobody understands** — each phase includes a human review pass; ADRs record the reasoning behind
  every numerical and architectural choice.
- **Wrong numbers** — independent QuantLib reference fixtures and property tests; golden files are hook-protected from agent edits.
- **Public demo cost / abuse** — rate limits, budget cap, replay mode, no secrets in the repo.
- **.NET 10 / Aspire package drift** — central package management, pinned SDK via `global.json`.

## 9. Open decisions

1. **Front end** — recommended: React + TypeScript. Alternative: Blazor, all-C#.
2. **Hosting** — recommended: Azure Container Apps. Alternative: local-only.
