namespace CurveRisk.Analytics.Curves;

/// <summary>
/// How the curve fills the gaps between pillars. Every scheme reprices the calibration instruments;
/// they differ in the forward rates they imply in between, and so in where bucketed risk appears.
/// </summary>
public enum InterpolationScheme
{
    /// <summary>Linear in log discount factor: piecewise-constant forward rates. Robust; forwards jump at pillars.</summary>
    LogLinearDiscount,

    /// <summary>Linear in the continuously compounded zero rate. Simple; forwards have a saw-tooth shape.</summary>
    LinearZero,

    /// <summary>
    /// Monotone cubic (Fritsch-Carlson) in log discount factor: smooth forwards, and discount factors
    /// stay decreasing wherever the pillar values are, so forwards stay positive.
    /// </summary>
    MonotoneCubicLogDiscount,
}

/// <summary>
/// Interpolates log discount factor against time. Nodes include the origin (time 0, log DF 0), so the
/// first segment starts from today. Beyond the last pillar the zero rate is held flat.
/// </summary>
internal static class Interpolation
{
    public static double LogDiscount(InterpolationScheme scheme, double[] times, double[] logDiscounts, double time)
    {
        var last = times.Length - 1;
        if (time >= times[last])
        {
            return logDiscounts[last] / times[last] * time;
        }

        return scheme switch
        {
            InterpolationScheme.LogLinearDiscount => Linear(times, logDiscounts, time),
            InterpolationScheme.LinearZero => LinearZero(times, logDiscounts, time),
            InterpolationScheme.MonotoneCubicLogDiscount => MonotoneCubic(times, logDiscounts, time),
            _ => throw new ArgumentOutOfRangeException(nameof(scheme), scheme, "Unknown interpolation scheme."),
        };
    }

    /// <summary>Index k such that xs[k] &lt;= x &lt; xs[k + 1].</summary>
    private static int Segment(double[] xs, double x)
    {
        var index = Array.BinarySearch(xs, x);
        var lower = index >= 0 ? index : ~index - 1;
        return Math.Min(Math.Max(lower, 0), xs.Length - 2);
    }

    private static double Linear(double[] xs, double[] ys, double x)
    {
        var k = Segment(xs, x);
        var weight = (x - xs[k]) / (xs[k + 1] - xs[k]);
        return ys[k] + (weight * (ys[k + 1] - ys[k]));
    }

    private static double LinearZero(double[] times, double[] logDiscounts, double time)
    {
        // Before the first pillar the zero rate is flat at the first pillar's rate.
        if (time <= times[1])
        {
            return logDiscounts[1] / times[1] * time;
        }

        var k = Segment(times, time);
        var zeroLeft = -logDiscounts[k] / times[k];
        var zeroRight = -logDiscounts[k + 1] / times[k + 1];
        var weight = (time - times[k]) / (times[k + 1] - times[k]);
        return -(zeroLeft + (weight * (zeroRight - zeroLeft))) * time;
    }

    private static double MonotoneCubic(double[] xs, double[] ys, double x)
    {
        var slopes = Slopes(xs, ys);
        var k = Segment(xs, x);
        var width = xs[k + 1] - xs[k];
        var u = (x - xs[k]) / width;

        // Cubic Hermite basis on [0, 1].
        var u2 = u * u;
        var u3 = u2 * u;
        return (((2 * u3) - (3 * u2) + 1) * ys[k])
            + ((u3 - (2 * u2) + u) * width * slopes[k])
            + (((-2 * u3) + (3 * u2)) * ys[k + 1])
            + ((u3 - u2) * width * slopes[k + 1]);
    }

    /// <summary>
    /// Node derivatives chosen so the interpolant has no overshoot: zero where the data turns, and a
    /// weighted harmonic mean of the neighbouring secants elsewhere.
    /// </summary>
    private static double[] Slopes(double[] xs, double[] ys)
    {
        var n = xs.Length;
        var secants = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
        {
            secants[i] = (ys[i + 1] - ys[i]) / (xs[i + 1] - xs[i]);
        }

        var slopes = new double[n];
        slopes[0] = secants[0];
        slopes[n - 1] = secants[n - 2];
        for (var i = 1; i < n - 1; i++)
        {
            slopes[i] = HarmonicSlope(secants[i - 1], secants[i], xs[i] - xs[i - 1], xs[i + 1] - xs[i]);
        }

        return slopes;
    }

    private static double HarmonicSlope(double left, double right, double widthLeft, double widthRight)
    {
        if (left * right <= 0)
        {
            return 0;
        }

        var weightLeft = (2 * widthRight) + widthLeft;
        var weightRight = widthRight + (2 * widthLeft);
        return (weightLeft + weightRight) / ((weightLeft / left) + (weightRight / right));
    }
}
