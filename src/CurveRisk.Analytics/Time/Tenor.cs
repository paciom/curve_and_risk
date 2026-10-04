using System.Globalization;

namespace CurveRisk.Analytics.Time;

public enum TenorUnit
{
    Days,
    Weeks,
    Months,
    Years,
}

/// <summary>A length of time as the market quotes it: "3M", "10Y".</summary>
public readonly record struct Tenor(int Count, TenorUnit Unit)
{
    public static Tenor Months(int count) => new(count, TenorUnit.Months);

    public static Tenor Years(int count) => new(count, TenorUnit.Years);

    /// <summary>Parses "2D", "1W", "6M", "10Y" (case-insensitive).</summary>
    public static Tenor Parse(string text)
    {
        var trimmed = (text ?? throw new ArgumentNullException(nameof(text))).Trim();
        if (trimmed.Length < 2
            || !int.TryParse(trimmed.Substring(0, trimmed.Length - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || count <= 0)
        {
            throw new FormatException($"'{text}' is not a tenor. Expected a positive count and a unit, such as 3M or 10Y.");
        }

        return new Tenor(count, ParseUnit(char.ToUpperInvariant(trimmed[trimmed.Length - 1]), text));
    }

    /// <summary>The unadjusted date this tenor after <paramref name="date"/>. Month ends clamp (31 Jan + 1M = 28/29 Feb).</summary>
    public DateTime AddTo(DateTime date) => Unit switch
    {
        TenorUnit.Days => date.Date.AddDays(Count),
        TenorUnit.Weeks => date.Date.AddDays(7 * Count),
        TenorUnit.Months => date.Date.AddMonths(Count),
        TenorUnit.Years => date.Date.AddYears(Count),
        _ => throw new InvalidOperationException($"Unknown tenor unit {Unit}."),
    };

    public override string ToString() => Count.ToString(CultureInfo.InvariantCulture) + Unit.ToString().Substring(0, 1);

    private static TenorUnit ParseUnit(char unit, string original) => unit switch
    {
        'D' => TenorUnit.Days,
        'W' => TenorUnit.Weeks,
        'M' => TenorUnit.Months,
        'Y' => TenorUnit.Years,
        _ => throw new FormatException($"'{original}' has an unknown tenor unit. Use D, W, M or Y."),
    };
}
