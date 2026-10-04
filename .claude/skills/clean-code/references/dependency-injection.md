# Dependency injection

The point of dependency injection is not the container. It is that a class states what it needs, receives it from outside, and can therefore be tested, replaced and reasoned about on its own.

## The rule

A class may create its own values and data structures. It may not create or look up its own services. Anything that does I/O, holds state shared with others, or is non-deterministic comes in through the constructor.

```csharp
// Hidden dependencies: untestable without a real database, a real clock and a real SMTP server.
public class InvoiceReminder
{
    public void SendOverdue()
    {
        var db = new SqlInvoiceStore(Config.ConnectionString);
        foreach (var invoice in db.Unpaid().Where(i => i.DueDate < DateTime.Now))
            new SmtpClient("mail").Send(Compose(invoice));
    }
}

// Declared dependencies: each can be replaced; the logic is testable in memory.
public sealed class InvoiceReminder(IInvoiceStore invoices, IMailSender mail, TimeProvider clock)
{
    public async Task SendOverdueAsync(CancellationToken cancellationToken)
    {
        var today = clock.GetUtcNow();
        foreach (var invoice in await invoices.UnpaidAsync(cancellationToken))
        {
            if (invoice.DueDate < today)
                await mail.SendAsync(Compose(invoice), cancellationToken);
        }
    }
}
```

## Guidelines

- **Constructor injection** for everything required. Property and method injection only for genuinely optional collaborators.
- **One composition root.** Wiring happens once, at application start. No other code references the container.
- **No service locator.** `ServiceProvider.GetService<T>()` inside a class hides its dependencies as thoroughly as `new` does.
- **Interfaces belong to the consumer.** Define `IInvoiceStore` beside the code that uses it, with only the members that code calls. Small interfaces are easy to fake and hard to misuse.
- **Do not invent an interface for everything.** Abstract what crosses a boundary (network, disk, clock, another team's code) or has more than one real implementation. A pure calculation class does not need an interface.
- **Inject non-determinism.** Time (`TimeProvider`), randomness, GUID generation, environment variables. This is what makes time-dependent logic testable.
- **Lifetimes are part of the design.** A singleton must be thread-safe and must not capture a scoped or transient dependency. A scoped service (per request) must not be held by a singleton.
- **Options, not configuration.** Inject a typed, validated options object. Classes do not read configuration keys by name.
- **Factories for runtime values.** When a dependency needs a value known only at call time, inject a factory or pass the value to the method; do not pass the container.

## Smells

| Smell | What it usually means |
|---|---|
| More than five constructor parameters | The class has several responsibilities; split it |
| A dependency used by one method only | That method belongs elsewhere |
| A test needs eight mocks | Same as above, seen from the test |
| `static` service with mutable state | A hidden singleton; make it an injected instance |
| Passing a dependency through three layers that do not use it | The middle layers are in the wrong place, or a smaller abstraction is missing |
