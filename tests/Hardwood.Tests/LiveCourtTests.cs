using Hardwood.Core.Engine;
using Hardwood.Desktop.Controls;
using Xunit;

namespace Hardwood.Tests;

/// <summary>The live court (Danny, run 45): every shot lands on the right half, inside the court, in its zone.</summary>
public class LiveCourtTests
{
    [Fact]
    public void Shots_land_on_the_attacking_half_in_their_zone()
    {
        for (int seq = 0; seq < 400; seq++)
        foreach (var zone in Enum.GetValues<ShotZone>())
        foreach (bool home in new[] { true, false })
        {
            var p = new LiveShot(home, zone, seq % 2 == 0, seq).Spot;
            Assert.InRange(p.X, 0, 94);
            Assert.InRange(p.Y, 0, 50);
            Assert.True(home ? p.X > 47 : p.X < 47, $"{zone} {home} {p}");
            double bx = home ? 94 - 5.25 : 5.25;
            double r = Math.Sqrt((p.X - bx) * (p.X - bx) + (p.Y - 25) * (p.Y - 25));
            switch (zone)
            {
                case ShotZone.Rim: Assert.True(r <= 5, $"rim {r}"); break;
                case ShotZone.CornerThree: Assert.True(p.Y < 4 || p.Y > 46, $"corner {p}"); break;
                case ShotZone.AboveBreakThree: Assert.True(r >= 23.5, $"three {r}"); break;
                default: Assert.InRange(r, 5, 23); break;
            }
            Assert.Equal(p, new LiveShot(home, zone, true, seq).Spot); // the same shot stays put
        }
    }
}
