---
name: refactor-safely
description: Restructure existing code without changing its behaviour - splitting large files and functions, extracting classes, introducing dependency injection, removing duplication. Use when asked to refactor, clean up, simplify or split code, or when a change is hard to make because of the code's current shape.
---

# Refactor safely

Refactoring changes structure and leaves behaviour alone. The moment behaviour changes too, a failing test can no longer tell you which of the two went wrong. So the discipline is: tests green, one small structural step, tests green again.

## Before the first edit

1. **Establish the safety net.** Run the tests and confirm they pass. Check they cover the code you are about to move: break a line on purpose and see something go red.
2. **No net? Build one first.** Write characterisation tests that pin down what the code does today, including behaviour that looks wrong. Do not fix it yet; record it.
3. **Agree the scope.** Name what is being restructured and what is not. A refactor that grows to touch everything cannot be reviewed.
4. **Separate it from feature work.** Refactoring goes in its own commit, before or after the behaviour change, never mixed in.

## The loop

1. Pick the smallest step that improves the structure.
2. Make it, preferably with the IDE's or compiler's automated refactoring.
3. Build and run the tests.
4. Green: commit, or at least note the checkpoint. Red: undo the step; do not debug forward.
5. Repeat.

## Common moves

| Problem | Move |
|---|---|
| Long function | Extract the steps into named functions; leave the original as a readable sequence of calls |
| Function doing two things | Split by responsibility; separate computing from I/O |
| Deep nesting | Guard clauses; extract the loop body; replace a conditional chain with a lookup or polymorphism |
| Large file or class | Group members by which fields they use; each group that shares fields is a class waiting to be extracted |
| `new` of a service inside logic | Introduce a constructor parameter and an interface; move construction to the composition root |
| Static or global state | Wrap it in an instance, inject the instance, then remove the static |
| Duplicated logic | Make the copies identical first, then extract once. Do this on the third occurrence, not the second |
| Long parameter list | Introduce a parameter object, or notice that the parameters are a missing type |
| Boolean flag parameter | Two functions |
| Primitive carrying meaning | Introduce a small value type |
| Comment explaining a block | Extract the block into a function named after the comment |

## Rules

- Public behaviour, signatures and error types stay the same unless changing them is the stated task. Check every caller when a signature must move.
- Do not rewrite when you can transform. A rewrite discards the edge cases the old code learned.
- Do not change a test's assertions during a refactor. If a test must change, the behaviour changed.
- Leave unrelated code alone, however tempting. Note it for the user.
- Stop when the change you actually need to make has become easy. Refactoring is a means.

## Finish

Run the full suite, the formatter and the size check from `clean-code`. Summarise what moved where and confirm in plain words that behaviour is unchanged and how you know.
