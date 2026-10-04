# ADR 0002: Graph workflows beside the agent loop

Status: proposed, 2026-10-04. Decision 4 needs the owner's agreement because it touches invariant 1.

## Context

The Risk Copilot is a loop: the model picks each tool call and decides when it is done ([ADR 0001](0001-ai-layer-architecture.md)). That suits open questions. It is a poor fit for a procedure that is the same every time, such as a risk brief of the whole book: the model spends a call on every step it could have been told, takes a different path on each run, and has to be trusted to cover every trade.

Graph engineering is the alternative for that kind of work. The procedure is written as a directed graph in code. Nodes do one step each, edges say what may follow, a typed state is the only thing passed between them, and the model fills the nodes that need language without choosing the path.

## Decisions

### 1. A small hand-written runtime, in its own project

`CurveRisk.Workflows` holds the builder, the validator, the runner and a checkpoint store, about 450 lines, with no reference to any other project or to an AI package. An architecture test keeps it that way.

It is hand-written for the reason the agent loop is: the guarantees live in the control flow, and a runtime this small can be read and tested in full. Microsoft Agent Framework's workflows were the alternative. They bring durable checkpointing and tooling, at the cost of a large dependency and a second set of model abstractions beside `IModelClient`. If briefs ever need to survive a restart or run across instances, that trade should be looked at again.

### 2. A graph is validated before it can run

`GraphBuilder.Build` rejects a graph with no start, a duplicate name, an edge to something undeclared, a node nothing leads to, or a node from which no end can be reached, and reports every problem at once. Each rule has a test that breaks only that rule.

A cycle is allowed when it has a way out. Whether it takes that way out depends on the state, which cannot be known at build time, so the runner also has a step limit and reports `StepLimitReached` by name.

### 3. Every exit is named, and the path is recorded

A run ends at a named end, stops at a named pause, hits the step limit, or faults because a node threw. The result carries the state, the outcome and a trace of every node executed with its duration. A node that throws is not swallowed: the exception comes back in the result and the API turns it into a 500, because it is a defect and not something a caller did.

### 4. The brief adds engine figures in code

The brief reports book totals: total PV, total DV01, the DV01 ladder summed by pillar, and the profit and loss of each shock summed over the book. `IRiskEngine` has no portfolio operations, so the `find_flags` node adds the per-trade figures the engine returned, rounds to the engine's two decimals, and refuses to add amounts in different currencies.

ADR 0001 says that when users need a combined figure, the engine gets an operation that returns it. This decision does not follow that. The sums are taken by deterministic, tested code and never by the model, the model is told not to compute, and its text is checked against the engine results and those sums. Invariant 1 as written in `CLAUDE.md` ("figures reach users only via `IRiskEngine`") is therefore met in spirit and not to the letter.

The better home for the sums is a portfolio operation on `IRiskEngine`, reviewed as pricing code. That is the intended follow-up; until then this is a known exception, and it is the owner's to accept or reject.

### 5. The existing guard rails are reused, not rebuilt

Nodes reach the engine through `ToolCatalog` and `ToolExecutor`, so the approval gate, the tool telemetry and the record of every call are the ones the loop uses. The narrate node calls `IModelClient`, is costed against the same `BudgetGuard`, and its text passes the same `NumericGrounding` with one repair before it is dropped. Repairs count as model calls and stay under `MaxModelCalls`. The node has no tools and its system prompt is a static file, so the cached prefix is stable.

A brief does not fail because the model does. With no provider configured, no budget, a refusal, a timeout or a provider error, the brief is returned with its figures and a commentary status that says why there is no paragraph.

### 5a. The model is shown a fixed, small set of figures, and only those are evidence

The check is the same as the loop's, but how much it proves depends on how many figures count as evidence: every extra value is one more an invented number can match by chance. The first version counted every raw tool result. A reviewer measured it on a 50-trade book: about one invented figure in three was accepted, and a trade's notional, which whoever books the trade chooses and the model never saw, could be stated as the book's loss and pass.

So the model is given the book totals, the trade count, the bucket ladder, the four scenario lines and the three flags, and the check uses exactly that JSON. It is not given per-trade rows, so the number of figures does not grow with the book. It is not given trade ids, because other people write them: an id is the one free-text field that would otherwise sit in the model's input, and an id shaped like an amount could be quoted back as one. The tables name the trades; the paragraph does not.

What remains: `NumericGrounding` accepts a rounding of any evidence value and tries each at one hundred times and one hundredth, so an invented figure can still match by chance. The evidence is now a few dozen values (39 on the eight-pillar demo curve; it grows with the number of pillars, not with the number of trades), so that chance no longer depends on the size of the book, but it is not zero and has not been measured since the change. The check does not detect figures spelled out in words or written in full-width digits; the first is a documented limit of the checker, the second was found in this review and is not yet fixed.

### 6. A person approves by resuming a paused run

A brief asked to offer a save stops at a pause after proposing the write, and the API returns an approval id. Posting a decision to that id resumes the run at the save step, which calls `save_scenario` through the approval gate. The gate for that leg approves only a call whose tool and arguments are the proposal the person was shown. A brief that is never resumed writes nothing, and an approval id works once. A book with no losing scenario is not offered a save. An approved save that the engine rejects ends at `SaveFailed`, which is not the same answer as a decline.

Checkpoints are held in memory, at most 100 and for at most 15 minutes, and keep only what the save step and the final response need. They do not survive a restart and are not shared between instances. The decision does not reload the book, so a book that changed after the pause cannot make it fail; a request cancelled mid-decision still uses up the id.

### 7. How the brief appears over HTTP

A brief is computed in the request and returns 200. `EmptyPortfolio` and `WithoutCommentary` are true answers and stay in the body. The engine refusing the content (an unpriceable trade, two trade ids that differ only by case, mixed currencies) and a failed save are 422, as on the other calculation routes. A book over 50 trades is 422 with its own problem type. At most two briefs run at once by default (`RiskBriefs:MaxConcurrent`); another is answered 429. A cancelled request stops between groups of engine calls.

Holding the connection for a brief follows `/copilot/answers`, not the job resource `/risk-runs` uses. A job resource would also give a pending approval a proper parent; that is the shape to move to if briefs become slow.

The trace and the graph endpoint describe the implementation. They are documented as diagnostic, and their node names may change without a new API version.

## Consequences

- The same request takes the same path, uses one model call (two with a repair), and the figures in its tables never pass through the model.
- A graph is rigid on purpose. A request that does not fit the brief still belongs to the Copilot loop.
- The API has no authentication, so whoever holds an approval id can approve, and the client that asked for the brief is the one given the id. The id is 128 random bits and single use; it identifies a pending decision, not a person. This is the first route that can reach a write tool over HTTP. Before `save_scenario` stores anything durable, approval has to be tied to an authenticated user.
- `save_scenario` writes to the engine's in-memory store, and the API builds an engine per request, so a scenario saved through a brief does not outlive the request that saved it, and every approved save returns the same id. The contract and the page say so. Persisting scenarios is separate work, and until it is done the save shows the control flow and nothing more.
- The daily model budget is checked before a call and recorded after it, so parallel requests can overshoot it. That is how `/copilot/answers` already behaves; the brief is a second route with the same property, limited by the concurrency cap.
- The decision is not retry-safe: if the response is lost, a retry gets 404 and cannot tell whether the save happened.
- The eval dataset has no cases for the brief. It is protected from agent edits, so those are for a person to add.
