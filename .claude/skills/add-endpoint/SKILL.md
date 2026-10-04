---
name: add-endpoint
description: Add or change a REST endpoint in the versioned ASP.NET Core API, contract first, with contract and integration tests. Use when asked to expose a resource or operation over HTTP, change a request or response shape, or introduce a new API version.
---

# Add an endpoint

Applies to `src/CurveRisk.Api` and `src/CurveRisk.Contracts` (PLAN.md phase 2). The contract is the product: TypeScript, Python and Excel clients are generated from the OpenAPI document, so a careless change breaks three codebases.

## Order of work

1. **Contract.** Request and response records in `CurveRisk.Contracts` (netstandard2.0). Units in names (`NotionalAmount`, `RatePercent`), enums as strings, timestamps as ISO 8601 UTC, dates as `yyyy-MM-dd`. Required properties are required; nothing is nullable "just in case".
2. **Is this breaking?** Removing or renaming a field, changing a type, tightening validation, or changing a default is breaking and goes in a new version (`/api/v2/...`), leaving v1 behaviour intact. Adding an optional request field or a response field is not. If in doubt, ask the `api-contract-guardian` subagent before writing the handler.
3. **Handler.** Thin: bind, validate, call one application-layer use case, map. `CancellationToken` passed through. No analytics calls on the request thread for anything that can take more than about 100 ms: create a job resource and return `202 Accepted` with `Location`.
4. **Errors.** RFC 9457 problem details with a stable `type` URI per error. Validation is 400 with field-level errors, unknown id is 404, concurrency conflict is 409 or 412, never 500 for caller mistakes.
5. **Resource semantics.** `POST` that creates accepts an `Idempotency-Key`. Mutable resources return an `ETag` and honour `If-Match`. Collections use cursor pagination. Calibrated curves and completed risk runs are immutable.
6. **Tests**
   - Integration: `WebApplicationFactory` against Testcontainers PostgreSQL. Happy path, each documented error, idempotent replay, stale `If-Match`.
   - Contract: the OpenAPI snapshot test will fail. Read the diff. You cannot accept it yourself (`*.verified.*` is hook-protected); show the diff to the user.
   - Compatibility: for a new version, a test that the previous version's request still produces the previous version's response shape.
7. **Clients.** Regenerate TypeScript and Python clients from the OpenAPI document. Never hand-edit files under `Generated/`.

## Review

Ask `api-contract-guardian` to review with the OpenAPI diff and the reason for the change.
