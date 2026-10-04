using System.Text.Json;
using System.Text.RegularExpressions;

namespace CurveRisk.Copilot;

/// <param name="Text">The number as written in the answer, for reporting back to the model and the user.</param>
/// <param name="Value">Numeric value after applying any scale word, suffix or exponent. NaN when the scale could not be interpreted.</param>
/// <param name="Tolerance">How far evidence may be from <see cref="Value"/> and still support it.</param>
/// <param name="IsBasisPoints">The claim carried a bp suffix, so evidence held as a fraction is also acceptable.</param>
public sealed record NumberClaim(string Text, double Value, double Tolerance, bool IsBasisPoints);

public sealed record GroundingReport(IReadOnlyList<string> Ungrounded)
{
    public bool IsGrounded => Ungrounded.Count == 0;
}

/// <summary>
/// Deterministic check that every number in a model answer can be traced to evidence. This is the
/// enforcement behind "the model never produces a number".
///
/// What counts as evidence:
///   * numeric JSON values in successful tool results;
///   * numbers in the user's own messages;
///   * identifiers ("T-1001") and ISO dates that appear verbatim in any tool result or user message.
///     These may be quoted back but their digits are not numbers: a trade id does not support a PV.
/// Free-text fields in tool results (descriptions, names) are not numeric evidence, because other
/// people write them.
///
/// The check fails closed. Every digit run in the answer is a claim, whatever precedes it, unless it is
/// a small count, a tenor or a quoted identifier. A number with a magnitude word the parser does not
/// know ("7 trillion", "2 x 10^6", "19 lakh") is rejected outright rather than read as a small count.
///
/// Precision: a figure shown with fewer than three significant digits must equal the evidence exactly
/// ("3.5%" for 3.5, "50bp" for 50). Otherwise it may be a rounding of the evidence, and an unscaled
/// figure may also be a truncation ("1,672,655" for 1,672,655.53).
///
/// Known limits, by design:
///   * Magnitudes are compared, not signs. "A loss of 1,200" and "-1,200" must both pass.
///   * The same quantity may be held as a fraction and quoted as a percentage, so evidence is also
///     tried at x100 and /100.
///   * Arithmetic on tool results (a sum of two deltas) is rejected. The prompt tells the model not to do it.
///   * Counts up to 10 and tenors ("5Y", "10-year") are not claims.
///   * Numbers written out in words ("two million") are not detected.
///   * A model that passes an invented number as a tool argument can see it echoed in the result.
/// </summary>
public static partial class NumericGrounding
{
    /// <param name="answer">The model's final text.</param>
    /// <param name="evidenceSources">Successful tool results (JSON) and user messages (text).</param>
    /// <param name="identifierSources">Extra text whose identifiers and dates may be quoted, such as tool error messages.</param>
    public static GroundingReport Check(
        string answer,
        IEnumerable<string> evidenceSources,
        IEnumerable<string>? identifierSources = null)
    {
        var sources = evidenceSources.ToList();
        var evidence = ExtractEvidence(sources);
        var quotable = sources.Concat(identifierSources ?? [])
            .SelectMany(source => IdentifierOrDatePattern().Matches(source).Select(m => m.Value))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var withoutQuotedIdentifiers = IdentifierOrDatePattern().Replace(answer, m => quotable.Contains(m.Value) ? " " : m.Value);

        var ungrounded = ExtractClaims(withoutQuotedIdentifiers)
            .Where(claim => !IsSupported(claim, evidence))
            .Select(claim => claim.Text)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return new GroundingReport(ungrounded);
    }

    /// <summary>True when the answer states <paramref name="value"/>, to the precision rules above.</summary>
    public static bool Mentions(string answer, double value) =>
        ExtractClaims(answer).Any(claim => IsSupported(claim, [value]));

    public static IReadOnlyList<NumberClaim> ExtractClaims(string text) =>
    [
        .. ClaimPattern().Matches(text)
            .Select(match => NumberToken.From(match, text))
            .Where(token => !token.IsTenor && !token.IsSmallCount)
            .Select(token => new NumberClaim(token.Label, token.Value, token.Tolerance, token.IsBasisPoints)),
    ];

    public static IReadOnlyList<double> ExtractEvidence(IEnumerable<string> sources)
    {
        var values = new List<double>();
        foreach (var source in sources)
        {
            if (TryParseJson(source, out var document))
            {
                using (document)
                {
                    Walk(document!.RootElement, values);
                }
            }
            else
            {
                // User text. Identifier and date digits are not figures.
                var text = IdentifierOrDatePattern().Replace(source, " ");
                values.AddRange(ExtractClaims(text).Select(claim => claim.Value).Where(double.IsFinite));
            }
        }

        return values;
    }

    private static bool IsSupported(NumberClaim claim, IReadOnlyList<double> evidence)
    {
        if (!double.IsFinite(claim.Value))
        {
            return false;
        }

        var target = Math.Abs(claim.Value);
        var tolerance = claim.Tolerance + (target * 1e-9) + 1e-12;

        return evidence
            .SelectMany(item => Readings(Math.Abs(item), claim.IsBasisPoints))
            .Any(reading => Math.Abs(reading - target) <= tolerance);
    }

    /// <summary>
    /// The ways one stored quantity may legitimately be quoted: as is, as a percentage of a fraction or
    /// the reverse, and in basis points when the claim says so.
    /// </summary>
    private static IEnumerable<double> Readings(double magnitude, bool allowBasisPoints)
    {
        yield return magnitude;
        yield return magnitude * 100;
        yield return magnitude / 100;
        if (allowBasisPoints)
        {
            yield return magnitude * 1e4;
        }
    }

    private static void Walk(JsonElement element, List<double> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                values.Add(element.GetDouble());
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Walk(item, values);
                }

                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Walk(property.Value, values);
                }

                break;
        }
    }

    private static bool TryParseJson(string text, out JsonDocument? document)
    {
        document = null;
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.IsEmpty || (trimmed[0] != '{' && trimmed[0] != '['))
        {
            return false;
        }

        try
        {
            document = JsonDocument.Parse(text);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // Shapes that may be quoted verbatim when they appear in evidence. ASCII digits only throughout:
    // \d would also match full-width and other Unicode digits, which double.Parse rejects.
    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9]*-[0-9]+\b|\b[0-9]{4}-[0-9]{2}-[0-9]{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierOrDatePattern();

    // Any digit run, whatever precedes it. Thousands may be grouped by comma, apostrophe or space.
    // The optional trailing word is captured so the caller can tell a scale ("million"), a unit ("bp"),
    // an unknown magnitude ("lakh") and an ordinary word ("trades") apart.
    [GeneratedRegex(
        @"(?<![0-9])(?<![0-9][.,'])[-\u2212+]?(?<ccy>\$|(?:USD|EUR|GBP|JPY|CHF|AUD|CAD)\s?)?[-\u2212+]?" +
        @"(?<num>(?:[0-9]{1,3}(?:[,'\u00A0\u202F ][0-9]{3})+|[0-9]+)(?:\.[0-9]+)?|\.[0-9]+)" +
        @"(?<exp>e[-+]?[0-9]+)?" +
        @"(?:(?<tenor>y\b|[-\s]?(?:years?|yrs?|months?)\b)|\s?(?<pct>%)|\s?(?<word>[a-z]+))?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClaimPattern();
}
