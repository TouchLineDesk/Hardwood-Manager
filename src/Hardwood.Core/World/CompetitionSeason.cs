namespace Hardwood.Core.World;

/// <summary>One game of a non-NABL competition. Scores are null until played.</summary>
public sealed class Fixture
{
    public int Index { get; set; }
    public DateOnly Date { get; set; }
    public int HomeId { get; set; }
    public int AwayId { get; set; }
    public int? HomePts { get; set; }
    public int? AwayPts { get; set; }
    /// <summary>"Regular season" or the playoff round's name.</summary>
    public string Stage { get; set; } = CompetitionSeason.RegularSeason;
    public int? SeriesId { get; set; }

    public bool Played => HomePts != null;
    public int? WinnerId => !Played ? null : HomePts > AwayPts ? HomeId : AwayId;
}

/// <summary>A playoff series (best-of-1 for Final Four games).</summary>
public sealed class SeriesState
{
    public int Id { get; set; }
    public int RoundIndex { get; set; }
    public string Round { get; set; } = "";
    public int HighId { get; set; }
    public int LowId { get; set; }
    public int BestOf { get; set; }
    public int HighWins { get; set; }
    public int LowWins { get; set; }

    public int WinsNeeded => BestOf / 2 + 1;
    public bool Complete => HighWins >= WinsNeeded || LowWins >= WinsNeeded;
    public int? WinnerId => !Complete ? null : HighWins > LowWins ? HighId : LowId;
    public int? LoserId => !Complete ? null : HighWins > LowWins ? LowId : HighId;
    public string Score => $"{Math.Max(HighWins, LowWins)}-{Math.Min(HighWins, LowWins)}";
}

/// <summary>A player's line for one competition season (kept light: background games produce these too).</summary>
public sealed class StatLine
{
    public int TeamId { get; set; }
    public int G { get; set; }
    public double Min { get; set; }
    public int Pts { get; set; }
    public int Reb { get; set; }
    public int Ast { get; set; }
    public int Stl { get; set; }
    public int Blk { get; set; }
    public int Fgm { get; set; }
    public int Fga { get; set; }
    public int Tpm { get; set; }
    public int Tpa { get; set; }

    public double Ppg => G == 0 ? 0 : (double)Pts / G;
    public double Rpg => G == 0 ? 0 : (double)Reb / G;
    public double Apg => G == 0 ? 0 : (double)Ast / G;
    public double Mpg => G == 0 ? 0 : Min / G;
}

/// <summary>A finished season of a competition.</summary>
public sealed record ChampionRecord(int Season, int ChampionId, int RunnerUpId, string ChampionName, string RunnerUpName,
    string? ThirdPlaceName = null);

/// <summary>A standings row computed from regular-season fixtures.</summary>
public sealed record TableRow(int TeamId, int Wins, int Losses, int PointsFor, int PointsAgainst)
{
    public int Games => Wins + Losses;
    public double Pct => Games == 0 ? 0 : (double)Wins / Games;
    public int Diff => PointsFor - PointsAgainst;
}

/// <summary>The state of one competition's current season. Serialized as JSON into the save.</summary>
public sealed class CompetitionSeason
{
    public const string RegularSeason = "Regular season", Complete = "Complete";

    public int Year { get; set; }
    public List<int> Participants { get; set; } = new();
    public List<Fixture> Fixtures { get; set; } = new();
    public List<SeriesState> Series { get; set; } = new();
    public string Stage { get; set; } = RegularSeason;
    public int? ChampionId { get; set; }
    public int? RunnerUpId { get; set; }
    /// <summary>The third-place tournament's winner and runner-up (the division format, D36).</summary>
    public int? ThirdPlaceId { get; set; }
    public int? ThirdPlaceRunnerUpId { get; set; }
    public Dictionary<int, StatLine> Stats { get; set; } = new();

    public bool IsComplete => Stage == Complete;

    public List<TableRow> Table()
    {
        var rows = Participants.ToDictionary(id => id, id => (w: 0, l: 0, pf: 0, pa: 0));
        foreach (var f in Fixtures)
        {
            if (!f.Played || f.Stage != RegularSeason) continue;
            var h = rows[f.HomeId]; var a = rows[f.AwayId];
            bool homeWon = f.HomePts > f.AwayPts;
            rows[f.HomeId] = (h.w + (homeWon ? 1 : 0), h.l + (homeWon ? 0 : 1), h.pf + f.HomePts!.Value, h.pa + f.AwayPts!.Value);
            rows[f.AwayId] = (a.w + (homeWon ? 0 : 1), a.l + (homeWon ? 1 : 0), a.pf + f.AwayPts!.Value, a.pa + f.HomePts!.Value);
        }
        return rows.Select(kv => new TableRow(kv.Key, kv.Value.w, kv.Value.l, kv.Value.pf, kv.Value.pa))
            .OrderByDescending(r => r.Pct).ThenByDescending(r => r.Diff).ThenBy(r => r.TeamId).ToList();
    }
}
