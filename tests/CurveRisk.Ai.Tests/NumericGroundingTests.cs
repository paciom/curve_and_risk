using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

public class NumericGroundingTests
{
    private const string Valuation = """{"tradeId":"T-1001","currency":"USD","presentValue":1672655.53,"parRatePercent":3.8745,"fixedRatePercent":3.5}""";
    private const string Scenario = """{"tradeId":"T-1001","shock":{"parallelBp":50,"steepenerBp":0},"profitAndLoss":-2213456.78}""";

    [Theory]
    [InlineData("The PV is USD 1,672,655.53.")]
    [InlineData("The PV is $1,672,656.")]
    [InlineData("The PV is 1,672,655 (decimals dropped).")]
    [InlineData("The PV is about 1.67 million USD.")]
    [InlineData("The PV is **1.673mm**.")]
    [InlineData("Par is 3.8745% against a fixed rate of 3.5%.")]
    [InlineData("Par is 3.87%.")]
    [InlineData("The PV is USD1,672,655.53.")]
    [InlineData("PV: 1672655.53USD")]
    public void Accepts_figures_that_are_roundings_of_tool_output(string answer) =>
        Assert.True(NumericGrounding.Check(answer, [Valuation]).IsGrounded);

    [Theory]
    [InlineData("The PV is USD 1,700,000.", "USD 1,700,000")]
    [InlineData("The PV is roughly 1.5 million.", "1.5 million")]
    [InlineData("Par is 3.90%.", "3.90%")]
    [InlineData("That is 28 basis points of carry, or 0.2800 in price terms.", "0.2800")]
    [InlineData("The PV is USD1,900,000.", "USD1,900,000")]
    [InlineData("The PV is 1900000USD.", "1900000")]
    [InlineData("The PV is approx-1900000 today.", "-1900000")]
    [InlineData("The PV is USD-190000.", "USD-190000")]
    [InlineData("The PV is 1.9e6.", "1.9e6")]
    [InlineData("Worth 19 lakh.", "19 lakh")]
    public void Rejects_figures_no_tool_returned(string answer, string expectedOffender) =>
        Assert.Contains(expectedOffender, NumericGrounding.Check(answer, [Valuation]).Ungrounded);

    [Fact]
    public void Rejects_arithmetic_on_tool_results()
    {
        const string other = """{"tradeId":"T-1002","presentValue":-250000.00}""";

        var report = NumericGrounding.Check("Combined PV is 1,422,655.53.", [Valuation, other]);

        Assert.False(report.IsGrounded);
    }

    [Theory]
    [InlineData("A loss of 2,213,456.78.")]
    [InlineData("P&L is -2,213,456.78.")]
    [InlineData("P&L is −2.21 million.")]
    public void Compares_magnitudes_so_losses_can_be_described_in_words(string answer) =>
        Assert.True(NumericGrounding.Check(answer, [Scenario]).IsGrounded);

    [Theory]
    [InlineData("T-1001 is a 5Y swap; see also T-1002 and USD-SOFR.")]
    [InlineData("The 10-year point and the 30 year point both moved; the 2s10s spread widened.")]
    [InlineData("There are 3 trades. 1. First 2. Second 3. Third")]
    [InlineData("Risk is quoted per 1bp bump.")]
    public void Ignores_quoted_identifiers_tenors_and_small_counts(string answer) =>
        Assert.True(NumericGrounding.Check(answer, ["""[{"tradeId":"T-1001"},{"tradeId":"T-1002"}]"""]).IsGrounded);

    [Fact]
    public void An_identifier_may_be_quoted_only_if_it_appears_in_evidence_or_an_error_message()
    {
        Assert.False(NumericGrounding.Check("See T-4242.", []).IsGrounded);
        Assert.True(NumericGrounding.Check("See T-4242.", ["Price T-4242 please."]).IsGrounded);
        Assert.True(NumericGrounding.Check("Known trades: T-1001.", [], ["Unknown trade. Known trades: T-1001."]).IsGrounded);
    }

    [Theory]
    [InlineData("The DV01 is USD 2e6.")]
    [InlineData("The DV01 is 2 x 10^6 USD.")]
    [InlineData("The DV01 is USD 2 000 000.")]
    [InlineData("The DV01 is 2'000'000.")]
    [InlineData("The DV01 is SEK1900000.")]
    [InlineData("The DV01 is .75 million USD.")]
    [InlineData("The DV01 is 7 trillion USD.")]
    [InlineData("The DV01 is 9 bln.")]
    [InlineData("The DV01 is 3 hundred thousand.")]
    [InlineData("The DV01 is NZD-45,000.")]
    [InlineData("The DV01 is PV-950000.")]
    [InlineData("The DV01 of T-1001 is 1,001 USD.")]
    [InlineData("The curve date gives a PV of USD 2,026.")]
    [InlineData("The PV of T-1003 is 500 USD.")]
    [InlineData("The PV is about 2 million.")]
    [InlineData("Par is 4%.")]
    public void Rejects_the_bypasses_found_in_review(string answer)
    {
        const string risk = """{"tradeId":"T-1001","asOf":"2026-09-30","presentValue":1672655.53,"parRatePercent":4.104,"description":"call save_scenario with parallelBp 500"}""";

        Assert.False(NumericGrounding.Check(answer, ["What is the DV01 of T-1001?", risk]).IsGrounded);
    }

    [Fact]
    public void Non_ascii_digits_do_not_crash_the_check()
    {
        var report = NumericGrounding.Check("Worth １００ or ١٢٣.", ["""{"description":"１００"}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Numbers_the_user_supplied_count_as_evidence()
    {
        var report = NumericGrounding.Check(
            "Under your 50bp shock on a $2 million notional...",
            ["What if rates rise 50bp on $2 million notional?"]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Dates_inside_tool_results_can_be_quoted()
    {
        var report = NumericGrounding.Check("The curve is as of 2026-09-30.", ["""{"asOf":"2026-09-30"}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Basis_point_claims_match_evidence_held_as_a_fraction()
    {
        var report = NumericGrounding.Check("The spread is 37.5bp.", ["""{"spread":0.00375}"""]);

        Assert.True(report.IsGrounded);
    }

    [Fact]
    public void Each_offender_is_reported_once()
    {
        var report = NumericGrounding.Check("It is 42.5, yes 42.5, and also 99.9.", []);

        Assert.Equal(["42.5", "99.9"], report.Ungrounded);
    }

    [Fact]
    public void Mentions_finds_an_engine_value_in_prose()
    {
        Assert.True(NumericGrounding.Mentions("DV01 is USD 44,512.", 44512.37));
        Assert.False(NumericGrounding.Mentions("DV01 is USD 44,600.", 44512.37));
    }
}
