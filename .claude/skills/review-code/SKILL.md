---
name: review-code
description: Review a diff, pull request or set of files for correctness, design, maintainability and test quality, and report findings by severity. Use when asked to review code or a PR, before reporting your own change as done, and after another agent or person hands over work.
---

# Code review

A review answers one question: what could go wrong if this is merged? Style that a formatter can fix is not a finding. Findings are things that produce a wrong result, lose data, break a caller, or make the next change harder than it should be.

## How to review

1. **Understand the intent first.** Read the description, ticket or request. You cannot judge a change without knowing what it is for. If the intent is unclear, that is the first finding.
2. **Read the whole diff once** without commenting, to see its shape.
3. **Read around the diff.** Open the callers, the callees and the tests. Most defects are in how changed code meets unchanged code.
4. **Go through the passes below**, one concern at a time.
5. **Verify before you report.** Run the build and tests. For a suspected bug, trace it with concrete inputs or write a small failing test. State which findings you confirmed and which you inferred.
6. **Review independently.** When reviewing your own work, or work whose author's reasoning you have seen, hand it to a subagent that gets only the code and the requirement. An author's explanation makes a reviewer see what was meant instead of what was written.

## Passes

**Correctness**
- Does it do what the requirement says, including the cases the requirement implies but does not state?
- Boundaries: empty, null, zero, one, maximum, negative, duplicate, out of order.
- Error paths: what happens when each call fails? Is anything left half-done? Are exceptions swallowed?
- Concurrency: shared mutable state, check-then-act, async without await, missing cancellation, deadlock by blocking on async.
- Resources: everything opened is closed on every path.
- Off-by-one, inverted condition, wrong operator, wrong variable, copy-paste that was not fully edited.

**Design**
- Is the change in the right place? Does it respect the existing layering and dependency direction?
- One responsibility per unit; dependencies injected, not reached for (see `clean-code`).
- Is anything duplicated that already exists? Is anything abstracted that has only one use?
- Public surface: is anything exposed that should not be? Is this a breaking change for any caller?

**Readability**
- Could a new team member follow this without the author present?
- Names say what things mean; functions are small and at one level of abstraction.
- Comments explain why; no dead or commented-out code.

**Tests**
- Is every new behaviour tested, including failure cases?
- Would each test fail if the behaviour were broken? Tests that assert nothing, restate the implementation, or over-mock are findings (see `unit-testing`).
- Were existing tests weakened, deleted or skipped to make this pass?

**Security and data** (escalate to `review-security` for anything touching input handling, authentication, secrets, or data access)
- Untrusted input reaching a query, command, path, or HTML without validation or encoding.
- Secrets in code, config or logs. Personal data in logs.
- Authorisation checked on every new entry point.

**Operations**
- Failure visible: logged with context, not silently ignored.
- Performance: queries in loops, unbounded collections, work on a hot path, missing pagination.
- Migrations and config changes safe to deploy and to roll back.

## Reporting

Order by severity. For each finding give the location, what is wrong, a concrete scenario that triggers it, and a suggested fix.

| Severity | Meaning |
|---|---|
| Blocker | Wrong result, data loss, security hole, broken contract. Must be fixed before merge |
| Major | Likely bug under realistic conditions, missing test for important behaviour, design that will cost soon |
| Minor | Readability or maintainability improvement worth making |
| Question | Something you could not determine; ask rather than assume |

```
[Blocker] src/Orders/OrderService.cs:84
Refund amount is not checked against the captured amount.
Scenario: payment of 50, refund request of 60 -> 60 is refunded.
Fix: reject when amount > payment.Captured - payment.Refunded; add a test.
Verified: reproduced with a unit test.
```

- Say what you checked and found clean, in a line, so the reader knows the coverage of the review.
- Say what you could not verify and why.
- No praise padding, no restating the diff, no style nits the formatter would catch.
- If there are no findings, say so plainly.

## When it is your own change

Fix blockers and majors before reporting the work as done. Do not report "reviewed" if the review was reading your own diff with your own reasoning still in mind; either get an independent pass or say that it has not had one.
