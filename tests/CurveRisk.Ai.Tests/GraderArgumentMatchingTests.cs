using System.Text.Json;
using CurveRisk.Evals;

namespace CurveRisk.Ai.Tests;

/// <summary>How expected tool arguments are matched against the arguments the agent actually sent.</summary>
public class GraderArgumentMatchingTests
{
    [Theory]
    [InlineData("0", "0.000000001", true)]
    [InlineData("0", "-0.000000001", true)]
    [InlineData("0", "0.0000000011", false)]
    [InlineData("0", "-0.0000000011", false)]
    [InlineData("50", "50.5", false)]
    public void Numbers_match_within_a_tolerance_of_one_billionth_inclusive(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, IsSubset(expected, actual));
    }

    [Theory]
    [InlineData("50", "\"50\"")]
    [InlineData("50", "null")]
    [InlineData("\"50\"", "50")]
    [InlineData("\"T-1001\"", "null")]
    [InlineData("true", "\"true\"")]
    [InlineData("null", "0")]
    public void Values_of_different_kinds_do_not_match(string expected, string actual)
    {
        Assert.False(IsSubset(expected, actual));
    }

    [Theory]
    [InlineData("\"T-1001\"", "\"t-1001\"", true)]
    [InlineData("\"T-1001\"", "\"T-1002\"", false)]
    public void Strings_match_ignoring_case(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, IsSubset(expected, actual));
    }

    [Theory]
    [InlineData("true", "true", true)]
    [InlineData("true", "false", false)]
    [InlineData("null", "null", true)]
    [InlineData("[1,2]", "[1,2]", true)]
    [InlineData("[1,2]", "[2,1]", false)]
    public void Booleans_nulls_and_arrays_match_only_when_identical(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, IsSubset(expected, actual));
    }

    [Theory]
    [InlineData("""{"shock":{"parallelBp":50}}""", """{"shock":{"parallelBp":50,"steepenerBp":0},"tradeId":"T-1001"}""", true)]
    [InlineData("""{"shock":{"parallelBp":50}}""", """{"shock":{"parallelBp":25}}""", false)]
    [InlineData("""{"shock":{"parallelBp":50}}""", """{"shock":{"steepenerBp":50}}""", false)]
    [InlineData("""{"shock":{"parallelBp":50}}""", """{"shock":50}""", false)]
    [InlineData("""{"shock":{"parallelBp":50}}""", """{"shock":[50]}""", false)]
    [InlineData("{}", """{"anything":1}""", true)]
    [InlineData("{}", "5", false)]
    public void Nested_objects_are_matched_as_subsets_at_every_level(string expected, string actual, bool matches)
    {
        Assert.Equal(matches, IsSubset(expected, actual));
    }

    [Fact]
    public void Every_expected_property_must_match_not_just_the_first()
    {
        var matches = IsSubset("""{"tradeId":"T-1001","parallelBp":50}""", """{"tradeId":"T-1001","parallelBp":25}""");

        Assert.False(matches);
    }

    [Fact]
    public void A_path_walks_object_properties_and_array_indexes()
    {
        using var document = JsonDocument.Parse("""{"buckets":[{"delta":1.5},{"delta":-2.25}]}""");

        var resolved = Graders.Resolve(document.RootElement, "buckets.1.delta");

        Assert.Equal(-2.25, resolved.GetDouble());
    }

    [Fact]
    public void A_path_to_a_property_that_does_not_exist_is_an_error()
    {
        using var document = JsonDocument.Parse("""{"presentValue":1.5}""");

        Assert.Throws<KeyNotFoundException>(() => Graders.Resolve(document.RootElement, "parRate"));
    }

    private static bool IsSubset(string expected, string actual)
    {
        using var expectedDocument = JsonDocument.Parse(expected);
        using var actualDocument = JsonDocument.Parse(actual);
        return Graders.IsSubset(expectedDocument.RootElement, actualDocument.RootElement);
    }
}
