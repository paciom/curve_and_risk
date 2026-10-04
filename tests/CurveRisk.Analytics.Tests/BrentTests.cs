using CurveRisk.Analytics.Numerics;

namespace CurveRisk.Analytics.Tests;

/// <summary>
/// The solver's step choices, observed through the points it asks the function for. Expected points
/// are worked by hand from the textbook method: a secant step through the two bracket ends, an
/// inverse quadratic step once three distinct values are known, and bisection whenever the
/// interpolated point is not safely inside the three quarters of the bracket nearest the best estimate.
/// </summary>
public class BrentTests
{
    // On a straight line the first secant step is the root itself. In every case the root lies in
    // the part of the bracket where the step is accepted, so bisecting instead would cost more calls.
    [Theory]
    [InlineData(2.0, 6.0, 5.0)]          // best estimate is the upper end
    [InlineData(2.0, 6.0, 3.0)]          // best estimate is the lower end
    [InlineData(0.5, 4.5, 2.5625)]       // just inside the half of the bracket nearest the best estimate
    [InlineData(-3.0, 1.0, 0.0)]         // a bracket that straddles zero
    [InlineData(-6.0, -2.0, -5.0)]       // a bracket of negative numbers
    [InlineData(4.0, 8.0, 5.5)]          // nearer the middle than the far quarter, from the lower end
    [InlineData(-8.0, -4.0, -5.5)]       // the same below zero, from the upper end
    public void A_straight_line_is_solved_exactly_by_one_secant_step(double lower, double upper, double root)
    {
        var points = new List<double>();

        var solved = Brent.Solve(
            x =>
            {
                points.Add(x);
                return x - root;
            },
            lower,
            upper);

        Assert.Equal(root, solved);
        Assert.Equal([lower, upper, root], points);
    }

    [Fact]
    public void Inverse_quadratic_interpolation_lands_on_the_root_of_a_function_whose_inverse_is_a_parabola()
    {
        // f(x) = 2 sqrt(x) - 6 has the inverse x = (y + 6)^2 / 4 and the root 9. The ends give -2 and 2,
        // so the secant point is the midpoint 10; the parabola through x = 4, 10 and 16 then gives 9.
        var points = new List<double>();

        var solved = Brent.Solve(
            x =>
            {
                points.Add(x);
                return (2 * Math.Sqrt(x)) - 6;
            },
            4,
            16);

        Assert.Equal(9.0, solved, precision: 12);
        Assert.Equal([4.0, 16.0, 10.0], points.Take(3));
        Assert.Equal(9.0, points[3], precision: 12);
    }

    [Fact]
    public void A_bracket_exactly_as_wide_as_the_tolerance_is_already_solved_at_the_end_with_the_smaller_residual()
    {
        var evaluations = 0;

        var solved = Brent.Solve(
            x =>
            {
                evaluations++;
                return x - 0.25;
            },
            0,
            1,
            tolerance: 1);

        Assert.Equal(0.0, solved);
        Assert.Equal(2, evaluations);
    }

    [Fact]
    public void The_smallest_positive_residual_is_not_mistaken_for_a_root()
    {
        var jump = Brent.Solve(x => x < 0.3 ? -1 : double.Epsilon, 0, 1);

        Assert.Equal(0.3, jump, precision: 12);
    }

    [Theory]
    [InlineData(0.25, 2.0)]    // undefined at the lower end
    [InlineData(-1.0, 0.75)]   // undefined at the upper end
    public void A_function_that_is_not_a_number_at_one_end_only_is_refused(double definedFrom, double definedTo)
    {
        var ex = Assert.Throws<RootFindingException>(() => Brent.Solve(
            x => x < definedFrom || x > definedTo ? double.NaN : x - 0.5,
            0,
            1));

        Assert.Equal("The function is not a number at an end of [0, 1].", ex.Message);
    }

    [Fact]
    public void A_root_at_the_upper_end_is_returned_when_the_function_is_positive_at_the_lower_end()
    {
        Assert.Equal(3.0, Brent.Solve(x => 3 - x, 1, 3));
    }

    [Fact]
    public void A_sign_change_far_from_zero_is_located_to_the_spacing_of_doubles_there()
    {
        // Doubles near 1.2 million are 2.3e-10 apart, far coarser than the default tolerance of 1e-14.
        var jump = Brent.Solve(x => x < 1234567.125 ? -1 : 1, 0, 2_000_000);

        Assert.Equal(1234567.125, jump, precision: 8);
    }

    [Fact]
    public void A_tolerance_that_cannot_be_met_stops_at_the_iteration_limit_and_reports_the_last_residual()
    {
        // A jump through zero at x = 0 with no tolerance: the bracket always straddles zero, so it is
        // never narrow relative to its own best estimate, and 200 steps cannot shrink it to nothing.
        var evaluations = 0;

        var ex = Assert.Throws<RootFindingException>(() => Brent.Solve(
            x =>
            {
                evaluations++;
                return x < 0 ? -2 : 1;
            },
            -1,
            2,
            tolerance: 0));

        Assert.Equal("Brent did not converge in 200 iterations; last residual 1.", ex.Message);
        Assert.Equal(202, evaluations);
    }
}
