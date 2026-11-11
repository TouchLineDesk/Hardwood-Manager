using Hardwood.Core.Engine;
using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.World;

/// <summary>What it would take to buy a player out of his overseas contract.</summary>
public sealed record BuyoutTerms(bool Possible, long Buyout, long TeamPays, long PlayerPays, long NablSalary, string Note);

/// <summary>
/// Player movement between the NABL and the rest of the world, during the NABL offseason only (decision #54).
/// Buyouts: every overseas contract has a clause; the NABL team pays up to an allowance and the player covers the rest,
/// if the NABL deal is worth it to him (decision #53). Draft rights: NABL teams can stash international picks with an
/// overseas club and bring them over later (decision #55). Overseas clubs never go over their import limit (decision #56).
/// </summary>
public static class WorldMarket
{
    /// <summary>Most an NABL team may pay toward a buyout (the NBA's figure is about 0.55% of the cap).</summary>
    public static long Allowance(Finance fin) => (long)Math.Round(fin.SalaryCap * 0.0055 / 10_000) * 10_000;

    /// <summary>A player pays at most this share of his first NABL year's salary toward his own buyout.</summary>
    public const double PlayerShareLimit = 0.30;

    public static bool IsInternational(Player p) => p.Nationality is not ("USA" or "CAN");

    /// <summary>Wire the end-of-summer hook so overseas clubs sign from the pool before the unsigned retire.</summary>
    public static void Attach(GameWorld world, Offseason off, GameSimulator? sim)
    {
        off.BeforeFreeAgentCleanup = o =>
        {
            world.EndSummer(o.Season + 1, sim ?? new GameSimulator(), o.League.FreeAgents);
            // (A club may have been promoted into the NABL since it signed him.)
            foreach (var (p, to) in world.SignedAbroad.Where(x => x.player.CurrentAbility >= 100).OrderByDescending(x => x.player.CurrentAbility).Take(6))
                world.News.Add(($"{p.Name} signs in {world.Competitions.FirstOrDefault(c => c.Clubs.Contains(to))?.Profile.Short ?? "the " + Hardwood.Core.Names.Brand.Top}", $"{p.Name} ({p.PrimaryPosition}, {p.CurrentAbility} CA) joins {to.FullName}."));
        };
    }

    public static Team? ClubOf(GameWorld world, Player p) => p.TeamId is { } id ? world.TeamById(id) : null;

    /// <summary>The terms for an NABL team to sign this overseas player now.</summary>
    public static BuyoutTerms Terms(GameWorld world, Offseason off, Team team, Player p)
    {
        var fin = off.League.Finance;
        long nablSalary = ContractMarket.MarketSalary(p.CurrentAbility, p.Age(off.Date), fin);
        var route = off.SigningOption(team, nablSalary);
        if (route.Route == SigningRoute.Minimum) nablSalary = route.Salary;
        long buyout = p.Contract?.Buyout ?? 0;
        long teamPays = Math.Min(buyout, Allowance(fin));
        long playerPays = buyout - teamPays;
        if (ClubOf(world, p) == null) return new(false, 0, 0, 0, nablSalary, "He isn't under contract overseas.");
        if (p.DraftRightsTeamId is { } r && r != team.Id) return new(false, buyout, teamPays, playerPays, nablSalary, "Another team holds his draft rights.");
        if (route.Route == SigningRoute.None) return new(false, buyout, teamPays, playerPays, nablSalary, "You can't fit his salary (no cap room, exception or minimum deal).");
        if (team.Roster.Count >= fin.RosterMax) return new(false, buyout, teamPays, playerPays, nablSalary, "Your roster is full.");
        long overseasSalary = p.Contract?.Salary ?? 0;
        if (playerPays > 0 && (playerPays > nablSalary * PlayerShareLimit || nablSalary < overseasSalary * 1.5))
            return new(false, buyout, teamPays, playerPays, nablSalary, $"He would have to pay ${playerPays / 1e6:F2}M himself; the move isn't worth it to him.");
        return new(true, buyout, teamPays, playerPays, nablSalary, playerPays > 0 ? $"You pay ${teamPays / 1e6:F2}M, he pays ${playerPays / 1e6:F2}M." : $"You pay the ${teamPays / 1e6:F2}M buyout.");
    }

    /// <summary>Buy a player out of his overseas contract and sign him (also how a team brings over a stashed pick).</summary>
    public static bool BuyOut(GameWorld world, Offseason off, Team team, Player p)
    {
        if (off.Phase != OffseasonPhase.FreeAgency) return false;
        var terms = Terms(world, off, team, p);
        if (!terms.Possible) return false;
        var club = ClubOf(world, p)!;
        var oldContract = p.Contract;
        club.Roster.Remove(p);
        p.TeamId = null; p.Contract = null;
        off.League.FreeAgents.Add(p);
        if (!off.SignFreeAgent(team, p))
        {
            // Shouldn't happen (terms were checked); put him back.
            off.League.FreeAgents.Remove(p);
            p.TeamId = club.Id; p.Contract = oldContract; club.Roster.Add(p);
            return false;
        }
        world.BuyoutMoves.Add((p, club, team, terms.Buyout));
        return true;
    }


    /// <summary>Where a stashed pick would play: a club in his own country if it has a league here, else any club with import room.</summary>
    public static Team? StashClub(GameWorld world, Player p, Rng rng)
    {
        var options = world.Competitions.Where(c => c.Clubs.Count > 0)
            .SelectMany(c => c.Clubs.Select(t => (c, t)))
            .Where(x => x.t.Roster.Count < 15)
            .Select(x => (x.c, x.t, local: x.c.Profile.LocalNationalities.Contains(p.Nationality),
                          imports: x.t.Roster.Count(q => !x.c.Profile.LocalNationalities.Contains(q.Nationality))))
            .Where(x => x.local || x.imports < x.c.Profile.ForeignLimit)
            .ToList();
        if (options.Count == 0) return null;
        var home = options.Where(x => x.local).ToList();
        var pool = home.Count > 0 ? home : options;
        return pool[rng.Next(pool.Count)].t;
    }

    /// <summary>Why a team can't stash this pick, or null if it can.</summary>
    public static string? CannotStash(Offseason off, Team team, Player p)
    {
        if (off.Phase != OffseasonPhase.FreeAgency) return "Picks can be stashed once the draft is over.";
        if (!off.Report.Draft.Any(d => d.Team == team && d.Player == p)) return "Only this year's picks can be stashed.";
        if (p.TeamId != team.Id && !off.League.FreeAgents.Contains(p)) return "He has already signed elsewhere.";
        return null;
    }

    /// <summary>Send a draft pick to play overseas; the team keeps his rights and can bring him over in a later summer.</summary>
    public static bool Stash(GameWorld world, Offseason off, Team team, Player p, Rng rng)
    {
        if (CannotStash(off, team, p) != null) return false;
        var club = StashClub(world, p, rng);
        if (club == null) return false; // no club has room under its import limit
        team.Roster.Remove(p);
        off.League.FreeAgents.Remove(p);
        var comp = world.HomeOf(club);
        p.TeamId = club.Id;
        p.Contract = GameWorld.NewContract(p, comp.Profile, rng.Next(2, 4), off.Date, off.Season + 1);
        p.DraftRightsTeamId = team.Id;
        club.Roster.Add(p);
        world.Stashes.Add((p, club, team));
        return true;
    }

    /// <summary>Players abroad whose NABL rights this team holds.</summary>
    public static IEnumerable<Player> RightsHeld(GameWorld world, League league, Team team) =>
        world.Players.Concat(league.FreeAgents).Where(p => p.DraftRightsTeamId == team.Id);

    /// <summary>
    /// AI front offices: stash young international second-rounders and late picks; bring over rights players who've
    /// grown into NABL players; and buy out the best overseas players when they can fit them.
    /// </summary>
    public static void AiMoves(GameWorld world, Offseason off, int? humanTeamId)
    {
        var rng = new Rng(world.NextRngSeed());
        var fin = off.League.Finance;
        int firstMove = world.BuyoutMoves.Count, firstStash = world.Stashes.Count;

        foreach (var d in off.Report.Draft.Where(d => d.Team.Id != humanTeamId))
        {
            var p = d.Player;
            if (!IsInternational(p) || p.Age(off.Date) > 21 || p.CurrentAbility >= 95) continue;
            if (d.Round == 1 && d.Pick < 20) continue;
            if (rng.Chance(d.Round == 2 ? 0.45 : 0.3)) Stash(world, off, d.Team, p, rng);
        }

        foreach (var team in off.League.Teams.Where(t => t.Id != humanTeamId))
            foreach (var p in RightsHeld(world, off.League, team).Where(p => p.CurrentAbility >= 100).OrderByDescending(p => p.CurrentAbility).ToList())
            {
                if (off.League.FreeAgents.Contains(p)) { if (rng.Chance(0.7)) off.SignFreeAgent(team, p); continue; }
                var t = Terms(world, off, team, p);
                if (t.Possible && rng.Chance(0.6) && ContractMarket.Payroll(team) + t.NablSalary <= off.AiPayrollLimit(team, false)) BuyOut(world, off, team, p);
            }

        // Buyouts of established overseas players: rare, a handful a summer at most, best first.
        int made = 0;
        foreach (var p in world.Players.Where(p => p.DraftRightsTeamId == null && p.CurrentAbility >= GameWorld.NablCalibre + 8 && p.Age(off.Date) <= 30)
                                       .OrderByDescending(p => ContractMarket.Value(p, off.Date)).ToList())
        {
            if (made >= 2) break;
            if (!rng.Chance(0.35)) continue;
            var team = off.League.Teams.Where(t => t.Id != humanTeamId)
                .Select(t => (t, terms: Terms(world, off, t, p)))
                .Where(x => x.terms.Possible && x.terms.TeamPays <= Allowance(fin) && ContractMarket.Payroll(x.t) + x.terms.NablSalary <= off.AiPayrollLimit(x.t, false))
                .OrderByDescending(x => off.LastWinPct(x.t) + rng.NextDouble() * 0.4)
                .Select(x => x.t).FirstOrDefault();
            if (team != null && BuyOut(world, off, team, p)) made++;
        }

        foreach (var (p, club, team, buyout) in world.BuyoutMoves.Skip(firstMove))
            world.News.Add(($"{team.Abbreviation} buy out {p.Name}", $"{team.FullName} bought {p.Name} out of his contract with {club.FullName} (${buyout / 1e6:F2}M buyout)."));
        foreach (var (p, club, team) in world.Stashes.Skip(firstStash))
            world.News.Add(($"{team.Abbreviation} stash {p.Name}", $"{team.FullName} draft pick {p.Name} will play for {club.FullName}; {team.Abbreviation} keep his rights."));
    }
}
