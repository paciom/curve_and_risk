using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>What counts as evidence for a figure, and how far a figure may be from it.</summary>
public class NumericGroundingEvidenceTests
{
    private const string Fraction = """{"zeroRate":0.0378}""";

    [Theory]
    [InlineData("The zero rate is 0.0378.")]
    [InlineData("The zero rate is 3.78%.")]
    [InlineData("The zero rate is 378bp.")]
    public void A_fraction_may_be_quoted_as_is_as_a_percentage_or_in_basis_points(string answer) =>
        Assert.True(NumericGrounding.Check(answer, [Fraction]).IsGrounded);

    [Theory]
    [InlineData("The zero rate is 378.", "378")]
    [InlineData("The zero rate is 37.8%.", "37.8%")]
    [InlineData("The zero rate is 0.378.", "0.378")]
    [InlineData("The zero rate is 378%.", "378%")]
    [InlineData("The zero rate is 37.8bp.", "37.8bp")]
    [InlineData("The zero rate is 3.78 million.", "3.78 million")]
    [InlineData("The zero rate is 3.78e3.", "3.78e3")]
    public void A_fraction_rescaled_any_other_way_is_rejected(string answer, string offender) =>
        Assert.Equal([offender], NumericGrounding.Check(answer, [Fraction]).Ungrounded);

    [Fact]
    public void A_percentage_held_by_the_engine_may_be_quoted_as_a_fraction()
    {
        var report = NumericGrounding.Check("As a fraction the par rate is 0.038745.", ["""{"parRatePercent":3.8745}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void A_basis_point_figure_is_not_supported_by_an_unrelated_value()
    {
        var report = NumericGrounding.Check("The spread is 37.5bp.", ["""{"spread":0.5}"""]);

        Assert.Equal(["37.5bp"], report.Ungrounded);
    }

    [Fact]
    public void Numbers_nested_in_arrays_and_objects_are_evidence()
    {
        const string risk = """{"buckets":[{"tenorYears":10,"delta":-812.37},[[4410.25]]],"flags":[true,null,"n/a"]}""";

        var report = NumericGrounding.Check("The deltas are -812.37 and 4,410.25.", [risk]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Only_json_numbers_are_extracted_from_a_tool_result()
    {
        const string result = """{"id":"T-1001","live":true,"note":null,"description":"notional 123456","delta":7.25,"buckets":[2.5]}""";

        Assert.Equal([7.25, 2.5], NumericGrounding.ExtractEvidence([result]));
    }

    [Theory]
    [InlineData("""{"description":"notional 123456"}""")]
    [InlineData("""[{"description":"notional 123456"}]""")]
    [InlineData("""  {"description":"notional 123456"}""")]
    [InlineData("""["notional 123456"]""")]
    public void Free_text_inside_a_tool_result_is_not_numeric_evidence(string toolResult)
    {
        var report = NumericGrounding.Check("The notional is 123,456.", [toolResult]);

        Assert.Equal(["123,456"], report.Ungrounded);
    }

    [Theory]
    [InlineData("[urgent] what if the notional is 250000?")]
    [InlineData("{draft} what if the notional is 250000?")]
    [InlineData("What if the notional is 250000?")]
    public void A_user_message_is_read_as_text_even_when_it_starts_like_json(string message) =>
        Assert.Equal([250000], NumericGrounding.ExtractEvidence([message]));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_source_contributes_no_evidence(string source) =>
        Assert.Empty(NumericGrounding.ExtractEvidence([source]));

    [Fact]
    public void Identifiers_and_dates_in_a_user_message_are_not_figures()
    {
        var evidence = NumericGrounding.ExtractEvidence(["Price T-1001 as of 2026-09-30 on 250000 notional."]);

        Assert.Equal([250000], evidence);
    }

    [Fact]
    public void An_identifier_in_a_user_message_leaves_a_gap_so_its_neighbours_are_not_joined()
    {
        // Without the gap this would read as "5 %": a figure, not a count.
        var evidence = NumericGrounding.ExtractEvidence(["Move 5 T-1001% of the way."]);

        Assert.Empty(evidence);
    }

    [Fact]
    public void A_quoted_identifier_in_an_answer_leaves_a_gap_so_its_neighbours_are_not_joined()
    {
        var report = NumericGrounding.Check("Hedge 5 T-1001% of it.", ["""{"tradeId":"T-1001"}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void A_zero_figure_is_supported_by_a_zero_result()
    {
        var report = NumericGrounding.Check("The P&L is 0.00.", ["""{"profitAndLoss":0}"""]);

        Assert.True(report.IsGrounded);
    }

    // "150" allows one whole unit, plus a relative 1e-9 and an absolute 1e-12 for floating-point noise:
    // 1.000000150001 in all. The two results below are adjacent doubles either side of that limit.
    [Fact]
    public void Evidence_exactly_at_the_tolerance_supports_the_figure()
    {
        var report = NumericGrounding.Check("The delta is 150.", ["""{"delta":151.000000150001}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Evidence_one_step_beyond_the_tolerance_does_not_support_the_figure()
    {
        var report = NumericGrounding.Check("The delta is 150.", ["""{"delta":151.00000015000103}"""]);

        Assert.Equal(["150"], report.Ungrounded);
    }

    [Theory]
    [InlineData("1.67 million", 1674999.99, true)]
    [InlineData("1.67 million", 1675000.01, false)]
    [InlineData("3.87%", 3.8749, true)]
    [InlineData("3.87%", 3.8751, false)]
    [InlineData("44,512", 44512.99, true)]
    [InlineData("44,512", 44513.01, false)]
    [InlineData("3.5%", 3.5, true)]
    [InlineData("3.5%", 3.51, false)]
    public void Mentions_applies_the_rounding_allowed_by_the_precision_shown(string text, double value, bool expected) =>
        Assert.Equal(expected, NumericGrounding.Mentions(text, value));
}
