using Hardwood.Core.Model;

namespace Hardwood.Core.Generation;

public sealed class StaffGenerator
{
    private readonly Rng _rng;
    public StaffGenerator(Rng rng) => _rng = rng;

    public Staff Generate(League league, StaffRole role, DateOnly today, int quality)
    {
        int Attr(bool key) => (int)Math.Round(Math.Clamp(_rng.NextGaussian(key ? quality : quality - 4, 2.5), 1, 20));
        int id = league.NextStaffId++;
        string first = _rng.Pick(NamePool.FirstNames), last = _rng.Pick(NamePool.LastNames);
        // No made-up coach carries a real NBA name either (#263); the re-roll uses its own stream so the main one doesn't move.
        if (RealWorld.RealNames.IsReal(first, last))
        {
            var side = new Rng(unchecked(_rng.Seed * 0x9E3779B97F4A7C15UL ^ (ulong)id * 0xC2B2AE3D27D4EB4FUL));
            for (int tries = 0; tries < 12 && RealWorld.RealNames.IsReal(first, last); tries++) last = side.Pick(NamePool.LastNames);
        }
        var s = new Staff
        {
            Id = id,
            FirstName = first,
            LastName = last,
            BirthDate = today.AddYears(-_rng.Next(32, 66)).AddDays(-_rng.Next(0, 365)),
            Role = role,
            Coaching = Attr(role == StaffRole.AssistantCoach),
            Development = Attr(role == StaffRole.DevelopmentCoach),
            Fitness = Attr(role == StaffRole.StrengthCoach),
            Medical = Attr(role == StaffRole.Physio),
            Scouting = Attr(role == StaffRole.Scout),
        };
        s.Reputation = Math.Clamp(s.KeyAttribute + _rng.Next(-2, 3), 1, 20);
        s.Salary = SalaryFor(s);
        s.ContractYears = _rng.Next(1, 4);
        return s;
    }

    public static long SalaryFor(Staff s) => 300_000 + (long)(Math.Pow(s.KeyAttribute / 20.0, 2) * 4_200_000) / 10_000 * 10_000;

    /// <summary>Give every team a full staff and seed the market.</summary>
    public void Populate(League league, DateOnly today)
    {
        foreach (var t in league.Teams)
        {
            int quality = (int)Math.Round(Math.Clamp(_rng.NextGaussian(11, 2.5), 5, 17));
            foreach (var role in Enum.GetValues<StaffRole>())
            {
                var s = Generate(league, role, today, quality + _rng.Next(-2, 3));
                s.TeamId = t.Id;
                t.Staff.Add(s);
            }
        }
        for (int i = 0; i < 30; i++)
        {
            var role = Enum.GetValues<StaffRole>()[_rng.Next(5)];
            league.FreeStaff.Add(Generate(league, role, today, (int)Math.Round(Math.Clamp(_rng.NextGaussian(10, 3), 3, 18))));
        }
    }
}
