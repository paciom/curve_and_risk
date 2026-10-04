namespace CurveRisk.Analytics.Numerics;

/// <summary>Raised when a root cannot be bracketed or the iteration limit is reached.</summary>
public sealed class RootFindingException(string message) : Exception(message);

/// <summary>
/// Brent's method: bisection's guarantee of convergence with the speed of the secant method and
/// inverse quadratic interpolation when they behave. Needs a bracket and no derivative.
/// </summary>
public static class Brent
{
    private const int MaxIterations = 200;
    private const double MachineEpsilon = 2.220446049250313e-16;

    /// <summary>Finds x in [lower, upper] with f(x) = 0 to within <paramref name="tolerance"/> on x.</summary>
    public static double Solve(Func<double, double> function, double lower, double upper, double tolerance = 1e-14)
    {
        var state = new State(lower, upper, function(lower), function(upper));
        if (double.IsNaN(state.Fa) || double.IsNaN(state.Fb))
        {
            throw new RootFindingException($"The function is not a number at an end of [{lower}, {upper}].");
        }

        if (state.Fa * state.Fb > 0)
        {
            throw new RootFindingException(
                $"No sign change on [{lower}, {upper}]: f(lower)={state.Fa}, f(upper)={state.Fb}. The root is not bracketed.");
        }

        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            // The absolute tolerance alone would be below floating-point spacing for a large root,
            // so the interval is also accepted once it is as narrow as doubles near B can express.
            var resolution = (2 * MachineEpsilon * Math.Abs(state.B)) + tolerance;
            if (Math.Abs(state.Fb) < double.Epsilon || Math.Abs(state.B - state.A) <= resolution)
            {
                return state.B;
            }

            state.Step(function, tolerance);
        }

        throw new RootFindingException($"Brent did not converge in {MaxIterations} iterations; last residual {state.Fb}.");
    }

    /// <summary>The bracket [A, B] with B the best estimate, C the previous B, and the last two step sizes.</summary>
    private sealed class State
    {
        public State(double a, double b, double fa, double fb)
        {
            (A, B, Fa, Fb) = Math.Abs(fa) < Math.Abs(fb) ? (b, a, fb, fa) : (a, b, fa, fb);
            (C, Fc, D, UsedBisection) = (A, Fa, B - A, true);
        }

        public double A { get; private set; }

        public double B { get; private set; }

        public double Fa { get; private set; }

        public double Fb { get; private set; }

        private double C { get; set; }

        private double Fc { get; set; }

        private double D { get; set; }

        private bool UsedBisection { get; set; }

        public void Step(Func<double, double> function, double tolerance)
        {
            var candidate = Interpolate();
            UsedBisection = MustBisect(candidate, tolerance);
            if (UsedBisection)
            {
                candidate = (A + B) / 2;
            }

            var value = function(candidate);
            (D, C, Fc) = (C, B, Fb);
            if (Fa * value < 0)
            {
                (B, Fb) = (candidate, value);
            }
            else
            {
                (A, Fa) = (candidate, value);
            }

            if (Math.Abs(Fa) < Math.Abs(Fb))
            {
                (A, B, Fa, Fb) = (B, A, Fb, Fa);
            }
        }

        // Inverse quadratic interpolation when the three values are distinct, else the secant step.
        private double Interpolate() => Differ(Fa, Fc) && Differ(Fb, Fc)
            ? (A * Fb * Fc / ((Fa - Fb) * (Fa - Fc))) + (B * Fa * Fc / ((Fb - Fa) * (Fb - Fc))) + (C * Fa * Fb / ((Fc - Fa) * (Fc - Fb)))
            : B - (Fb * (B - A) / (Fb - Fa));

        private static bool Differ(double x, double y) => Math.Abs(x - y) > double.Epsilon;

        private bool MustBisect(double candidate, double tolerance)
        {
            var low = Math.Min(((3 * A) + B) / 4, B);
            var high = Math.Max(((3 * A) + B) / 4, B);
            var previousStep = UsedBisection ? Math.Abs(B - C) : Math.Abs(C - D);

            return candidate <= low || candidate >= high
                || Math.Abs(candidate - B) >= previousStep / 2
                || previousStep < tolerance;
        }
    }
}
