---
name: clean-code
description: Standards for writing and changing production code - small files, small single-purpose functions, clear names, dependency injection, explicit error handling, no hidden state. Use whenever writing new code, adding a feature, or modifying existing code in any language, before the first line is written.
---

# Clean code

Code is read far more often than it is written, and most of the readers are trying to change it without breaking it. Every rule here serves one aim: a reader can understand a unit of code without holding the rest of the system in their head.

The numbers below are defaults, not laws. Exceed one when you can say why in a sentence; if you cannot, split.

## Before writing

1. Read the neighbouring code. Match its naming, layout, error style and test style. Consistency with the codebase outranks any preference in this file.
2. State what the change must do and how you will know it works. If you cannot name the check, you do not understand the task yet.
3. Find the existing thing first. Search for a helper, type or pattern that already does this before adding a new one.

## Size

| Unit | Default limit | Why |
|---|---|---|
| Function | 30 lines, 3 levels of nesting, 4 parameters | Fits on a screen and in working memory; each is testable alone |
| File | 300 lines | One reason to open it |
| Class | One responsibility you can state without "and" | A change in one requirement touches one class |
| Line | The formatter's limit | Never argue with the formatter |

When a function grows past the limit, the fix is rarely "extract lines 20 to 40". Ask what the function is doing at two levels of abstraction at once, and separate the policy (what happens, in what order) from the mechanics (how each step is done).

Run the size check on what you changed:

```bash
node .claude/skills/clean-code/scripts/size-report.mjs src
```

## Functions

- **One job.** If the name needs "and", or a boolean parameter selects between two behaviours, it is two functions.
- **Guard clauses over nesting.** Handle the invalid and trivial cases first and return; keep the main path unindented.
- **Inputs in, result out.** No output parameters. No modifying arguments unless the name says so.
- **Separate deciding from doing.** Pure functions compute; a thin outer layer performs I/O. The pure part is where the logic and the tests live.
- **Same level of abstraction throughout.** A function that calls `ValidateOrder()` should not also parse a date by hand.

## Names

- Name things for what they mean to the caller, not how they are built: `overdueInvoices`, not `filteredList`.
- Units and conventions go in the name when the type cannot carry them: `timeoutSeconds`, `ratePercent`, `createdUtc`.
- Booleans read as assertions: `isExpired`, `hasAccess`.
- No abbreviations a new team member would have to ask about. No type prefixes.
- A name that needs a comment to explain it is the wrong name.

## Dependencies

Depend on abstractions you receive, not on concrete things you reach for. Full guidance: [references/dependency-injection.md](references/dependency-injection.md).

- Constructor injection for required collaborators. A class's constructor is the honest list of what it needs.
- No `new` on a service inside business logic, no static service locators, no ambient singletons.
- Inject the clock, the random source, the file system and the network. Anything non-deterministic is a dependency.
- Define the interface where it is consumed, sized to what the consumer uses.
- More than four or five constructor parameters means the class has more than one job.

## State and data

- Immutable by default. Make a field mutable only when something must change it, and keep that change in one place.
- Make invalid states unrepresentable: a type that cannot be constructed wrong needs no validation downstream.
- No primitive obsession. An `EmailAddress` or `Money` type removes a class of bugs a `string` or `decimal` invites.
- No global mutable state. No hidden coupling through statics.

## Errors

- Validate at the boundary (API, file, user input, another team's service), then trust the types inside.
- Fail fast and loudly with a message that says what was wrong and what was expected.
- Catch only what you can handle, at the level that can handle it. Never swallow an exception; never catch the base exception type to hide a problem.
- Exceptions for the exceptional; a result type or a nullable return for outcomes the caller is expected to handle.
- Clean up with the language's scoped construct (`using`, `try/finally`, `defer`, context managers), never by hoping.

## Comments

Comment the why: a constraint, a non-obvious decision, a workaround with its reason. Do not narrate what the code does, and do not leave commented-out code; version control remembers.

## What not to add

- No speculative generality. An interface with one implementation and no test double is not an abstraction; it is indirection.
- No configuration option without a second real use.
- No backwards-compatibility shim for code that has no other callers.
- No new dependency for something the standard library does.
- Nothing outside the scope of the task. Note unrelated problems for the user; do not fix them in the same change.

## Before calling it done

1. The project's quality gate passes (here: `node tools/check.mjs`). Analyzer findings are fixed in the code; never suppress a rule or lower a threshold to get through.
2. Every new behaviour has a test that fails without it (see the `unit-testing` skill).
3. Re-read your own diff top to bottom as a reviewer would (see `review-code`).
4. Language-specific points: [references/csharp.md](references/csharp.md).
