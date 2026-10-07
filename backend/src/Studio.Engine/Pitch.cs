namespace Studio.Engine;

/// <summary>
/// Pitch geometry and the core football metrics. Everything here is a pure function of
/// locations. The simulator uses the same functions to decide outcomes, so a pass the engine
/// rates as difficult really was less likely to succeed.
/// </summary>
public static class Pitch
{
    public const double Length = 105.0;
    public const double Width = 68.0;
    public const double GoalY = Width / 2;
    public const double GoalHalfWidth = 7.32 / 2;

    public static double Clamp(double v, double lo = 0, double hi = 1) => Math.Max(lo, Math.Min(hi, v));

    /// <summary>View a fixed-frame location from <paramref name="side"/>'s perspective.</summary>
    public static (double X, double Y) AttackingFrame(Side side, double x, double y) =>
        side == Side.Home ? (x, y) : (Length - x, Width - y);

    /// <summary>Inverse of <see cref="AttackingFrame"/> (the mirror is its own inverse).</summary>
    public static (double X, double Y) FixedFrame(Side side, double x, double y) => AttackingFrame(side, x, y);

    public static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    /// <summary>
    /// Rate a pass 0 (routine) to 1 (very hard), inputs in the passer's attacking frame.
    /// Four transparent factors: length, forward progress, how advanced the target is, and
    /// whether the passer was being pressed.
    /// </summary>
    public static double PassDifficulty(double sx, double sy, double ex, double ey, bool underPressure)
    {
        var length = Clamp(Distance(sx, sy, ex, ey) / 50);
        var progress = Clamp((ex - sx) / 40);
        var targetZone = Clamp((ex - 60) / 45);
        var pressure = underPressure ? 1.0 : 0.0;
        return Clamp(0.05 + 0.40 * length + 0.20 * progress + 0.20 * targetZone + 0.15 * pressure);
    }

    /// <summary>Angle in radians subtended by the goal mouth from (x, y), attacking frame.</summary>
    public static double GoalAngle(double x, double y)
    {
        var a1 = Math.Atan2(GoalY - GoalHalfWidth - y, Length - x);
        var a2 = Math.Atan2(GoalY + GoalHalfWidth - y, Length - x);
        return Math.Abs(a2 - a1);
    }

    /// <summary>
    /// Expected goals from a logistic model on angle and distance. Calibrated so a central
    /// penalty-spot shot is about 0.25, the six-yard box about 0.55 and a 20 m strike about 0.07.
    /// </summary>
    public static double ShotXg(double x, double y, bool underPressure, bool header)
    {
        var d = Distance(x, y, Length, GoalY);
        var logit = -0.371 + 1.102 * GoalAngle(x, y) - 0.1306 * d;
        if (underPressure) logit -= 0.4;
        if (header) logit -= 0.8;
        return 1 / (1 + Math.Exp(-logit));
    }

    /// <summary>A pass that moves the ball at least 10 m forward into the opponent's half.</summary>
    public static bool IsProgressive(double sx, double ex) => ex - sx >= 10 && ex > Length / 2;

    public static bool InBox(double x, double y) => x >= Length - 16.5 && Math.Abs(y - GoalY) <= 20.16;
}
