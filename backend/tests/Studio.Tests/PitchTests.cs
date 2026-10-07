using Studio.Engine;

namespace Studio.Tests;

public class PitchTests
{
    [Theory]
    [InlineData(94, 34, 0.18, 0.32)]   // penalty spot
    [InlineData(99.5, 34, 0.45, 0.65)] // six-yard box
    [InlineData(85, 34, 0.04, 0.10)]   // 20 m, central
    [InlineData(75, 34, 0.0, 0.03)]    // 30 m
    public void Xg_is_calibrated_to_familiar_chances(double x, double y, double lo, double hi)
    {
        Assert.InRange(Pitch.ShotXg(x, y, underPressure: false, header: false), lo, hi);
    }

    [Fact]
    public void Pressure_and_headers_lower_xg()
    {
        var clean = Pitch.ShotXg(94, 34, false, false);
        Assert.True(Pitch.ShotXg(94, 34, true, false) < clean);
        Assert.True(Pitch.ShotXg(94, 34, false, true) < clean);
    }

    [Fact]
    public void Tight_angles_lower_xg()
    {
        Assert.True(Pitch.ShotXg(100, 10, false, false) < Pitch.ShotXg(100, 34, false, false));
    }

    [Fact]
    public void Longer_forward_and_pressured_passes_are_harder()
    {
        var shortSideways = Pitch.PassDifficulty(40, 34, 40, 44, false);
        var longForward = Pitch.PassDifficulty(40, 34, 80, 40, false);
        Assert.True(longForward > shortSideways);
        Assert.True(Pitch.PassDifficulty(40, 34, 40, 44, true) > shortSideways);
        Assert.InRange(Pitch.PassDifficulty(5, 34, 104, 1, true), 0, 1);
    }

    [Fact]
    public void Attacking_frame_mirrors_for_the_away_side()
    {
        Assert.Equal((10.0, 20.0), Pitch.AttackingFrame(Side.Home, 10, 20));
        Assert.Equal((95.0, 48.0), Pitch.AttackingFrame(Side.Away, 10, 20));
        var (x, y) = Pitch.AttackingFrame(Side.Away, 10, 20);
        Assert.Equal((10.0, 20.0), Pitch.FixedFrame(Side.Away, x, y));
    }
}
