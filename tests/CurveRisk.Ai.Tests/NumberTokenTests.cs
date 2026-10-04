using CurveRisk.Copilot;

namespace CurveRisk.Ai.Tests;

/// <summary>
/// How a number written in prose is read: its value, how much rounding its precision allows, and
/// whether it is a figure at all. Driven through <see cref="NumericGrounding.ExtractClaims"/>.
/// </summary>
public class NumberTokenTests
{
    private static NumberClaim Claim(string text) => Assert.Single(NumericGrounding.ExtractClaims(text));

    [Theory]
    [InlineData("1,672,655.53", 1672655.53)]
    [InlineData("2'000'000", 2000000)]
    [InlineData("1.67 million", 1670000)]
    [InlineData("2.5k", 2500)]
    [InlineData("3bn", 3000000000)]
    [InlineData("1.9e6", 1900000)]
    [InlineData("2.5e-3", 0.0025)]
    [InlineData("$5", 5)]
    [InlineData("37.5bp", 37.5)]
    public void A_figure_is_read_with_its_separators_scale_word_and_exponent(string text, double expected)
    {
        var claim = Claim(text);

        Assert.Equal(expected, claim.Value, precision: 6);
        Assert.Equal(text, claim.Text);
    }

    [Theory]
    [InlineData("19 lakh")]
    [InlineData("7 zillion")]
    [InlineData("3 hundred")]
    [InlineData("2 x")]
    public void A_magnitude_word_the_parser_does_not_know_makes_the_figure_unreadable(string text)
    {
        var claim = Claim(text);

        Assert.Equal(double.NaN, claim.Value);
        Assert.Equal(0, claim.Tolerance);
        Assert.Equal(text, claim.Text);
    }

    [Theory]
    [InlineData("2 × 12")]
    [InlineData("2    × 12")]
    [InlineData("2× 12")]
    public void A_number_followed_by_a_multiplication_sign_is_unreadable_however_many_spaces_intervene(string text)
    {
        var claims = NumericGrounding.ExtractClaims(text);

        Assert.Equal(["2", "12"], claims.Select(c => c.Text));
        Assert.Equal([double.NaN, 12], claims.Select(c => c.Value));
    }

    [Fact]
    public void A_number_raised_to_a_power_is_unreadable()
    {
        var claim = Claim("10^6");

        Assert.Equal("10", claim.Text);
        Assert.Equal(double.NaN, claim.Value);
    }

    [Theory]
    [InlineData("3 trades")]
    [InlineData("10 trades")]
    [InlineData("1bp")]
    [InlineData("5Y")]
    [InlineData("10-year")]
    [InlineData("30 years")]
    [InlineData("6 months")]
    public void Small_counts_and_tenors_are_not_figures(string text) =>
        Assert.Empty(NumericGrounding.ExtractClaims(text));

    [Theory]
    [InlineData("11 trades", "11")]
    [InlineData("$5", "$5")]
    [InlineData("5%", "5%")]
    [InlineData("5 million", "5 million")]
    [InlineData("5.0", "5.0")]
    [InlineData("5e0", "5e0")]
    [InlineData("1900000USD", "1900000")]
    public void A_number_is_a_figure_once_it_is_large_or_carries_a_currency_percent_scale_decimals_or_exponent(
        string text, string expectedLabel) =>
        Assert.Equal(expectedLabel, Claim(text).Text);

    [Theory]
    [InlineData("50bp", true)]
    [InlineData("50 bps", true)]
    [InlineData("50 BPS", true)]
    [InlineData("50", false)]
    [InlineData("50%", false)]
    [InlineData("50 trades", false)]
    public void Only_a_bp_or_bps_suffix_marks_a_figure_as_basis_points(string text, bool expected) =>
        Assert.Equal(expected, Claim(text).IsBasisPoints);

    [Theory]
    [InlineData("3.5")]
    [InlineData("3.5%")]
    [InlineData("0.05%")]
    [InlineData("50bp")]
    [InlineData("2 million")]
    [InlineData("1.9e6")]
    public void A_figure_with_fewer_than_three_significant_digits_must_match_exactly(string text) =>
        Assert.Equal(0, Claim(text).Tolerance);

    [Theory]
    [InlineData("1,672,655", 1)]
    [InlineData("1,672,655.53", 0.01)]
    [InlineData("42.5", 0.1)]
    [InlineData("37.5bp", 0.1)]
    [InlineData("USD 44,512", 1)]
    public void An_unscaled_figure_may_be_off_by_a_whole_unit_of_its_last_digit(string text, double expected) =>
        Assert.Equal(expected, Claim(text).Tolerance, precision: 9);

    [Theory]
    [InlineData("3.87%", 0.005)]
    [InlineData("3.8745%", 0.00005)]
    [InlineData("1.67 million", 5000)]
    [InlineData("1.673mm", 500)]
    [InlineData("167 thousand", 500)]
    [InlineData("1.90e6", 5000)]
    [InlineData("190e4", 5000)]
    [InlineData("2.50e-3", 0.000005)]
    public void A_percentage_or_scaled_figure_may_be_off_by_half_a_unit_of_its_last_digit(string text, double expected) =>
        Assert.Equal(expected, Claim(text).Tolerance, precision: 9);
}
