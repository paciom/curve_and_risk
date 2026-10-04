# C# and .NET specifics

Additions to the general rules for C# code.

## Language

- Nullable reference types on; treat warnings as errors. A `!` needs the same justification as a cast.
- `sealed` by default. Inheritance is a design decision, not an accident.
- `record` for immutable data, `readonly record struct` for small values, `class` for things with identity or behaviour.
- `required` and `init` over constructors with many optional parameters.
- Pattern matching and switch expressions over `if`/`else if` chains on type or value.
- `internal` by default; `public` is an API commitment.
- File-scoped namespaces, one top-level type per file, file named after the type.

## Async

- Async all the way: no `.Result`, `.Wait()` or `GetAwaiter().GetResult()` on a path that can deadlock or starve the pool.
- Every async method that does I/O takes a `CancellationToken` and passes it on.
- `ConfigureAwait(false)` in library code.
- Never `async void` except for event handlers.
- CPU-bound work does not belong on a request thread; I/O-bound work does not belong in `Task.Run`.
- `ValueTask` only where a benchmark shows it matters.

## Resources and collections

- `using` / `await using` for everything `IDisposable` / `IAsyncDisposable`.
- `HttpClient` through `IHttpClientFactory`, never `new` per call.
- Return `IReadOnlyList<T>` or `IEnumerable<T>`; accept the least specific type that works.
- Do not enumerate an `IEnumerable<T>` twice; materialise once if you need to.
- `StringComparison` is explicit on every string comparison; culture is explicit on every parse and format.

## Errors

- Throw the most specific built-in exception (`ArgumentNullException`, `InvalidOperationException`) or a domain exception; never `Exception`.
- `ArgumentNullException.ThrowIfNull` and friends at public entry points.
- Exception filters (`when`) instead of catch-and-rethrow.
- `throw;` to rethrow, never `throw ex;`.

## Money and time

- `decimal` for money, never `double`.
- `DateTimeOffset` or `DateOnly` over `DateTime`; store and compute in UTC; inject `TimeProvider`.
