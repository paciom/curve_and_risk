---
name: unit-testing
description: Write unit tests that catch real bugs - what to test, how to structure a test, test doubles, edge cases, and how to raise coverage meaningfully. Use when writing tests for new or existing code, when asked to improve coverage, when a bug needs a regression test, or when reviewing whether tests are any good.
---

# Unit testing

A test suite exists to tell you, quickly and specifically, that a change broke something. A test that cannot fail, or that fails for reasons unrelated to behaviour, is a cost with no return. Coverage measures which code ran, not which behaviour was checked; treat it as a map of what is untested, never as the goal.

## Process

1. **List behaviours before writing tests.** From the requirement and the public signature, not from the implementation: what does it promise for valid input, at the boundaries, for invalid input, and when a dependency fails?
2. **Write the test first when fixing a bug.** Reproduce it as a failing test, watch it fail for the right reason, then fix.
3. **See every new test fail once.** Break the code or invert the assertion briefly. A test you have never seen fail is unverified.
4. **Run the whole suite**, not just the new tests, before finishing.

## What to cover

For each unit, work through this list and write down which apply:

- **Main path**: the example a domain expert would give.
- **Boundaries**: zero, one, many; empty and null; first and last; minimum and maximum; exactly at, just under and just over each limit.
- **Invalid input**: each documented rejection, asserting the error type and that the message is usable.
- **Dependency failure**: timeout, exception, empty result, partial result.
- **State**: called twice, called out of order, called concurrently if the type claims thread safety.
- **Invariants**: properties that hold for every input (round trips, ordering, totals that must add up). Use property-based tests where the framework has them.
- **Regression**: one test per bug fixed, named for the behaviour, not the ticket.

## Structure of a test

```csharp
[Fact]
public void Refund_larger_than_the_payment_is_rejected()
{
    // Arrange
    var payment = Payment.Captured(amount: 50m);

    // Act
    var result = payment.Refund(60m);

    // Assert
    Assert.Equal(RefundError.ExceedsPayment, result.Error);
}
```

- **Name states the behaviour** as a sentence. Someone reading only test names should learn what the unit does.
- **Arrange, act, assert**, visibly separated, one act per test.
- **One reason to fail.** Several assertions are fine when they describe one outcome.
- **No logic in tests.** No loops, conditionals or computed expectations; a test that recomputes the answer the same way the code does proves nothing. Expected values are literals or come from an independent source.
- **Everything relevant is visible in the test.** Use builders or factory methods to hide the irrelevant setup, not the values the assertion depends on.
- **Parameterise** when the same behaviour is checked over several inputs; separate tests when the behaviours differ.

## Test doubles

- Prefer the real object when it is fast and deterministic. Prefer a hand-written fake (an in-memory repository) over a mocking framework when the dependency has behaviour.
- Fake what crosses a boundary: network, disk, clock, randomness, another team's service. Do not mock types you own that are plain logic.
- Assert on outcomes (returned value, resulting state, message sent), not on which internal methods were called in which order. Interaction assertions are for when the interaction *is* the behaviour: "the email was sent once".
- If a test needs many mocks, the design is telling you something. See `clean-code`.

## Properties of a good suite

| Property | Means |
|---|---|
| Fast | The unit suite runs in seconds, so it gets run |
| Isolated | No shared state, no order dependence, no real network, clock or file system |
| Deterministic | Same result every run; a flaky test is a broken test and is fixed or removed, never retried |
| Specific | A failure points at one behaviour and says what was expected |
| Behavioural | Survives a refactor that does not change behaviour |

## Raising coverage

1. Produce the coverage report and sort by uncovered lines, not by percentage.
2. For each gap ask what behaviour is unverified, and write the test for that behaviour. Never write a test whose only purpose is to execute lines.
3. Prioritise by consequence: branching logic, error handling, money, security and concurrency before getters and wiring.
4. Leave untestable-by-design glue (composition root, generated code, thin entry points) uncovered and say so, or cover it with a small integration test.
5. Check the tests have teeth: mutate a condition or a constant in the code and confirm a test goes red. Use a mutation-testing tool where one exists for the language.

If the project enforces a coverage threshold, treat a failure as "which behaviour is untested?", never as "which lines can I execute?", and never lower the threshold or add an exclusion to pass.

## Mutation testing

Coverage says a line ran. A mutation tool changes the code (flips a comparison, removes a statement, swaps a constant) and reruns the tests: a mutant that no test notices has *survived*, and marks behaviour nobody is checking.

1. Run the tool the project uses (here: `dotnet stryker`; the report lands in `StrykerOutput/`).
2. Read the survivors, grouped by file. For each, ask what observable behaviour differs between the original and the mutant, and write the test that asserts it.
3. Some survivors are equivalent (the mutant behaves identically, such as changing `<` to `<=` where equality cannot occur). Say so and leave them; do not contort a test to kill one.
4. Never exclude a file or disable a mutator to raise the score.

## Never

- Weaken an assertion, widen a tolerance, or delete a test to get to green. A failing test is information: decide whether the code or the expectation is wrong, and say which.
- Assert on private state through reflection.
- Use `Thread.Sleep` or real time; inject the clock.
- Catch an exception in a test to keep it passing.
- Copy the implementation's formula into the expected value.
