namespace Shop.Catalog;

/// <summary>Registered as a singleton and used by every request.</summary>
public sealed class PriceCache(IPriceSource source, TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly Dictionary<string, (decimal Price, DateTimeOffset LoadedAt)> _entries = new();

    public async Task<decimal> GetAsync(string sku, CancellationToken cancellationToken)
    {
        if (_entries.TryGetValue(sku, out var entry) && clock.GetUtcNow() - entry.LoadedAt < Lifetime)
        {
            return entry.Price;
        }

        var price = await source.LoadAsync(sku, cancellationToken);
        _entries[sku] = (price, clock.GetUtcNow());
        return price;
    }

    public void Invalidate(string sku) => _entries.Remove(sku);
}
