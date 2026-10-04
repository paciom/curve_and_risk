namespace Shop.Common;

public static class Pagination
{
    /// <summary>Returns the items on a 1-based page.</summary>
    public static IReadOnlyList<T> Page<T>(IReadOnlyList<T> items, int pageNumber, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(pageNumber, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        var start = pageNumber * pageSize;
        if (start >= items.Count)
        {
            return [];
        }

        var count = Math.Min(pageSize, items.Count - start);
        return [.. items.Skip(start).Take(count)];
    }

    public static int PageCount(int itemCount, int pageSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(itemCount);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);

        return (itemCount + pageSize - 1) / pageSize;
    }
}
