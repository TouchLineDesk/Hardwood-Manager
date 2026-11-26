using Hardwood.Core.International;

namespace Hardwood.Core.Season;

public sealed partial class GameSession
{
    /// <summary>I6a (run 214): your national-team job and the open offers (never null; older saves get an empty one).</summary>
    public NationalCoaching NationalJobState => World.International.Coaching ??= new NationalCoaching();

    /// <summary>The nation you coach, or null.</summary>
    public NationalTeam? MyNation => NationalJobState.Code is { } code ? NationalTeams.Ranked(this).FirstOrDefault(t => t.Nation.Code == code) : null;

    /// <summary>I6a: once a day. Offers past their day lapse; from Oct 15 each year, a coach with no national team hears from up to
    /// two federations (by reputation, smaller nations first). Its own stream: nothing shared is drawn.</summary>
    private void NationalJobDay()
    {
        var job = NationalJobState;
        if (job.Offers.RemoveAll(o => o.Expires <= Today) > 0 && job.Offers.Count == 0 && job.Code == null)
            Post("National team offers lapsed", "The federations that called have moved on. Others may call next October.", "World");
        if (Today < NationalJob.OfferDate(Today.Year) || job.LastOfferYear >= Today.Year) return;
        job.LastOfferYear = Today.Year;
        if (job.Code != null) return;
        var offers = NationalJob.OffersFor(NationalTeams.Ranked(this), Coach.Reputation, Seed, Today);
        if (offers.Count == 0) return;
        job.Offers = offers;
        string list = string.Join("\n", offers.Select(o => $"• {o.Name}, No. {o.Rank} in the FIBA world ranking"));
        Post(offers.Count == 1 ? $"{offers[0].Name} want you as head coach" : "National teams want you as head coach",
            $"{(offers.Count == 1 ? "A federation has" : $"{offers.Count} federations have")} asked whether you'll coach their national team alongside "
            + (Employed ? $"your job at {HumanTeam.TheFullName}" : "whatever club job comes next") + $":\n\n{list}\n\n"
            + $"Club seasons don't stop: you pick the twelve for the qualifying windows and the summer tournaments, and the club keeps its own schedule. "
            + $"Federations call by reputation (yours is {Coach.Reputation:0}/100), so the smaller nations come first. "
            + $"Answer on the International page by {offers[0].Expires:MMM d}.", "World");
    }

    /// <summary>I6a: take a federation's offer. You start with the federation's own twelve as your picks.</summary>
    public bool AcceptNationalOffer(string code)
    {
        var job = NationalJobState;
        var offer = job.Offers.FirstOrDefault(o => o.Code == code);
        if (offer == null || offer.Expires < Today || job.Code != null) return false;
        var team = NationalTeams.Ranked(this).FirstOrDefault(t => t.Nation.Code == code);
        if (team == null) return false;
        job.Code = code;
        job.Name = team.Name;
        job.Since = Today;
        job.Picks = team.Squad.Select(x => x.Player.Id).ToList();
        job.Offers.Clear();
        Post($"You're the head coach of {team.Name}",
            $"{team.Name} (No. {team.Rank} in the world) are yours, alongside your club job. The federation's twelve are your picks for now: "
            + "change them on the International page before the next window or tournament. A pick who is hurt on the day, or no longer eligible, "
            + "is replaced by the next best player for that event.", "World");
        return true;
    }

    public bool DeclineNationalOffer(string code) => NationalJobState.Offers.RemoveAll(o => o.Code == code) > 0;

    /// <summary>I6a: step down from the national team.</summary>
    public bool ResignNationalJob()
    {
        var job = NationalJobState;
        if (job.Code == null) return false;
        job.Past.Add(new NationalSpell(job.Code, job.Name, job.Since ?? Today, Today));
        Post($"You've stepped down as head coach of {job.Name}", "The federation thanked you and will name a successor. Other nations may call next October.", "World");
        job.Code = null; job.Name = ""; job.Since = null; job.Picks.Clear();
        return true;
    }

    /// <summary>I6a: add a player to (or drop him from) your twelve. Only players in your nation's pool; at most twelve.</summary>
    public bool ToggleNationalPick(int playerId)
    {
        var job = NationalJobState;
        if (job.Code == null) return false;
        if (job.Picks.Remove(playerId)) return true;
        var team = MyNation;
        if (team == null || team.Pool.All(x => x.Player.Id != playerId)) return false;
        // Review (run 214): a pick who has left the pool (retired, gone from the world) no longer holds a place.
        var pool = team.Pool.Select(x => x.Player.Id).ToHashSet();
        job.Picks.RemoveAll(id => !pool.Contains(id));
        if (job.Picks.Count >= NationalTeams.SquadSize) return false;
        job.Picks.Add(playerId);
        return true;
    }

    /// <summary>I6a: go back to the federation's best twelve.</summary>
    public void ResetNationalPicks()
    {
        var job = NationalJobState;
        if (job.Code == null) return;
        job.Picks.Clear();
        if (MyNation is { } team) job.Picks = team.Squad.Select(x => x.Player.Id).ToList();
    }
}
