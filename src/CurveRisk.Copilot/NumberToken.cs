using System.Collections.Frozen;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CurveRisk.Copilot;

/// <summary>
/// One number as it was written: digits, optional exponent, and whatever followed it. Knows how to
/// read its own magnitude and how much rounding its displayed precision permits.
/// </summary>
internal sealed partial record NumberToken(
    string Text,
    string Mantissa,
    string Exponent,
    string Word,
    NumberMarks Marks)
{
    private const double SmallIntegerLimit = 10;
    private const int MinimumSignificantDigitsForRounding = 3;

    private static readonly FrozenDictionary<string, double> Scales = new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["k"] = 1e3,
        ["thousand"] = 1e3,
        ["thousands"] = 1e3,
        ["m"] = 1e6,
        ["mm"] = 1e6,
        ["mn"] = 1e6,
        ["mln"] = 1e6,
        ["mio"] = 1e6,
        ["million"] = 1e6,
        ["millions"] = 1e6,
        ["b"] = 1e9,
        ["bn"] = 1e9,
        ["bln"] = 1e9,
        ["billion"] = 1e9,
        ["billions"] = 1e9,
        ["t"] = 1e12,
        ["tn"] = 1e12,
        ["trn"] = 1e12,
        ["trillion"] = 1e12,
        ["trillions"] = 1e12,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static NumberToken From(Match match, string source) => new(
        Text: match.Value,
        Mantissa: SeparatorPattern().Replace(match.Groups["num"].Value, string.Empty),
        Exponent: match.Groups["exp"].Value,
        Word: match.Groups["word"].Value.ToLowerInvariant(),
        Marks: new NumberMarks(
            IsPercent: match.Groups["pct"].Success,
            HasCurrency: match.Groups["ccy"].Success,
            IsTenor: match.Groups["tenor"].Success,
            OperatorFollows: IsOperatorAt(source, match.Index + match.Length)));

    public bool IsTenor => Marks.IsTenor;

    public bool IsBasisPoints => Word is "bp" or "bps";

    public double? Scale => Scales.TryGetValue(Word, out var scale) ? scale : null;

    /// <summary>A magnitude word the parser does not know, or an operator that goes on to build a bigger number.</summary>
    public bool IsUninterpretable => Marks.OperatorFollows || (Scale is null && UnknownMagnitudePattern().IsMatch(Word));

    /// <summary>"3 trades", "1bp": an integer up to ten with nothing that makes it a figure.</summary>
    public bool IsSmallCount => IsBareInteger && !IsQualified && Raw <= SmallIntegerLimit;

    public double Value => IsUninterpretable ? double.NaN : Raw * (Scale ?? 1.0);

    /// <summary>
    /// Fewer than three significant digits: exact match only. Otherwise half a unit of the last digit
    /// (rounding), or a whole unit for an unscaled figure, which may also be a truncation.
    /// </summary>
    public double Tolerance
    {
        get
        {
            if (IsUninterpretable || SignificantDigits < MinimumSignificantDigitsForRounding)
            {
                return 0;
            }

            var unit = Math.Pow(10, -Decimals) * Multiplier;
            return IsScaled || Marks.IsPercent ? unit / 2 : unit;
        }
    }

    /// <summary>The number as shown to the model and the user, without a trailing ordinary word ("trades", "USD").</summary>
    public string Label
    {
        get
        {
            var keepsWord = Word.Length == 0 || HasMagnitudeWord || IsBasisPoints;
            return (keepsWord ? Text : Text[..Text.LastIndexOf(Word, StringComparison.OrdinalIgnoreCase)]).Trim();
        }
    }

    private bool HasMagnitudeWord => Scale is not null || IsUninterpretable;

    private bool IsScaled => Scale is not null || Exponent.Length > 0;

    private bool IsBareInteger => Decimals == 0 && Exponent.Length == 0;

    private bool IsQualified => HasMagnitudeWord || Marks.IsPercent || Marks.HasCurrency;

    private int Decimals =>
        Mantissa.Contains('.', StringComparison.Ordinal) ? Mantissa.Length - Mantissa.IndexOf('.', StringComparison.Ordinal) - 1 : 0;

    private double Raw => double.Parse(Mantissa + Exponent, NumberStyles.Float, CultureInfo.InvariantCulture);

    private int SignificantDigits => Mantissa.Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0').Length;

    private double Multiplier =>
        (Scale ?? 1.0) * (Exponent.Length == 0 ? 1.0 : Math.Pow(10, double.Parse(Exponent.AsSpan(1), CultureInfo.InvariantCulture)));

    private static bool IsOperatorAt(string text, int index)
    {
        while (index < text.Length && text[index] == ' ')
        {
            index++;
        }

        return index < text.Length && text[index] is '×' or '^';
    }

    [GeneratedRegex(@"[,'   ]", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorPattern();

    [GeneratedRegex(@"^(?:[a-z]*illions?|hundreds?|lakhs?|crores?|x|e)$", RegexOptions.CultureInvariant)]
    private static partial Regex UnknownMagnitudePattern();
}

/// <summary>What surrounded a number in the text.</summary>
internal readonly record struct NumberMarks(bool IsPercent, bool HasCurrency, bool IsTenor, bool OperatorFollows);
