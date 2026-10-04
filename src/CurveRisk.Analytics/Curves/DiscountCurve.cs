using CurveRisk.Analytics.Time;

namespace CurveRisk.Analytics.Curves;

/// <summary>
/// Discount factors by date. Curve time is ACT/365F from the as-of date; zero rates are continuously
/// compounded. Immutable: every bump or shift returns a new curve.
/// </summary>
public sealed class DiscountCurve
{
    private readonly double[] _nodeTimes;
    private readonly double[] _nodeLogDiscounts;

    public DiscountCurve(DateTime asOf, IReadOnlyList<DateTime> pillarDates, IReadOnlyList<double> discountFactors, InterpolationScheme scheme)
    {
        Validate(asOf, pillarDates, discountFactors);

        AsOf = asOf.Date;
        PillarDates = pillarDates.Select(date => date.Date).ToList();
        DiscountFactors = discountFactors.ToList();
        Scheme = scheme;

        PillarTimes = PillarDates.Select(TimeTo).ToList();

        // The origin is a node: discount factor 1 today.
        _nodeTimes = WithOrigin(PillarTimes);
        _nodeLogDiscounts = WithOrigin(DiscountFactors.Select(factor => Math.Log(factor)));
    }

    public DateTime AsOf { get; }

    public IReadOnlyList<DateTime> PillarDates { get; }

    public IReadOnlyList<double> DiscountFactors { get; }

    public InterpolationScheme Scheme { get; }

    /// <summary>Pillar times in years (ACT/365F from the as-of date).</summary>
    public IReadOnlyList<double> PillarTimes { get; }

    /// <summary>Years from the as-of date, ACT/365F.</summary>
    public double TimeTo(DateTime date) => DayCount.Act365Fixed.YearFraction(AsOf, date);

    public double DiscountFactor(DateTime date) => DiscountFactor(TimeTo(date));

    /// <summary>Discount factor at <paramref name="time"/> years. 1 for today and for dates in the past.</summary>
    public double DiscountFactor(double time) =>
        time <= 0 ? 1.0 : Math.Exp(Interpolation.LogDiscount(Scheme, _nodeTimes, _nodeLogDiscounts, time));

    /// <summary>Continuously compounded zero rate, as a fraction, to <paramref name="time"/> years (must be positive).</summary>
    public double ZeroRate(double time) => time > 0
        ? -Math.Log(DiscountFactor(time)) / time
        : throw new ArgumentOutOfRangeException(nameof(time), time, "A zero rate needs a positive time.");

    /// <summary>Simply compounded forward rate between two dates, as a fraction, under <paramref name="dayCount"/>.</summary>
    public double ForwardRate(DateTime start, DateTime end, DayCount dayCount)
    {
        // Past dates discount at 1, so a period starting before today would silently lose its elapsed part.
        if (start.Date < AsOf || end.Date <= start.Date)
        {
            throw new ArgumentException(
                $"A forward period must start on or after the curve date {AsOf:yyyy-MM-dd} and end after it starts; got {start:yyyy-MM-dd} to {end:yyyy-MM-dd}.",
                nameof(start));
        }

        return ((DiscountFactor(start) / DiscountFactor(end)) - 1.0) / dayCount.YearFraction(start, end);
    }

    /// <summary>A copy with one pillar's discount factor replaced. Used by calibration.</summary>
    public DiscountCurve WithDiscountFactor(int pillar, double discountFactor)
    {
        var factors = DiscountFactors.ToArray();
        factors[pillar] = discountFactor;
        return new DiscountCurve(AsOf, PillarDates, factors, Scheme);
    }

    /// <summary>
    /// A copy with each pillar's zero rate moved by <paramref name="shiftAtTime"/> (a fraction, as a
    /// function of the pillar time in years). Interpolation then carries the shift between pillars.
    /// </summary>
    public DiscountCurve ShiftZeroRates(Func<double, double> shiftAtTime)
    {
        var times = PillarTimes;
        var factors = DiscountFactors.Select((factor, i) => factor * Math.Exp(-shiftAtTime(times[i]) * times[i])).ToArray();
        return new DiscountCurve(AsOf, PillarDates, factors, Scheme);
    }

    /// <summary>A copy with a single pillar's zero rate moved by <paramref name="shift"/> (a fraction).</summary>
    public DiscountCurve BumpPillar(int pillar, double shift)
    {
        var time = PillarTimes[pillar];
        return WithDiscountFactor(pillar, DiscountFactors[pillar] * Math.Exp(-shift * time));
    }

    private static double[] WithOrigin(IEnumerable<double> values)
    {
        var nodes = new List<double> { 0.0 };
        nodes.AddRange(values);
        return nodes.ToArray();
    }

    private static void Validate(DateTime asOf, IReadOnlyList<DateTime> pillarDates, IReadOnlyList<double> discountFactors)
    {
        if (pillarDates.Count == 0 || pillarDates.Count != discountFactors.Count)
        {
            throw new ArgumentException("A curve needs at least one pillar and one discount factor per pillar.", nameof(pillarDates));
        }

        var dates = pillarDates.Select(date => date.Date).ToList();
        var outOfOrder = dates.Zip(dates.Skip(1), (earlier, later) => later <= earlier).Any(broken => broken);
        if (dates[0] <= asOf.Date || outOfOrder)
        {
            throw new ArgumentException("Pillar dates must be after the as-of date and strictly increasing.", nameof(pillarDates));
        }

        if (discountFactors.Any(factor => !(factor > 0) || double.IsInfinity(factor)))
        {
            throw new ArgumentException("Discount factors must be positive and finite.", nameof(discountFactors));
        }
    }
}
