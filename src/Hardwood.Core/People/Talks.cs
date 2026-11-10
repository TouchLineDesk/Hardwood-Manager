using Hardwood.Core.Model;
using Hardwood.Core.Season;

namespace Hardwood.Core.People;

public enum TalkTopic { Praise, Criticise, PromiseMinutes, DiscussRole, AskWhatsWrong, PromiseTrade, RefuseTrade, ConvinceToStay }

public sealed record TalkResult(string Reply, double MoraleChange, bool Positive);

/// <summary>
/// One-on-one conversations between the coach and a player (decision #64). What works depends on who he is:
/// professionals take criticism well, hot-heads don't; ambitious players with egos want minutes and money; loyal
/// players can be talked out of a trade request. Broken promises cost trust for three months.
/// </summary>
public static class Talks
{
    public const int CooldownDays = 7;

    public static string Label(TalkTopic t) => t switch
    {
        TalkTopic.Praise => "Praise his play",
        TalkTopic.Criticise => "Criticise his play",
        TalkTopic.PromiseMinutes => "Promise more minutes",
        TalkTopic.DiscussRole => "Discuss his role",
        TalkTopic.AskWhatsWrong => "Ask how he's feeling",
        TalkTopic.PromiseTrade => "Promise to trade him",
        TalkTopic.RefuseTrade => "Refuse to trade him",
        _ => "Convince him to stay",
    };

    public static IEnumerable<TalkTopic> Available(Player p) => p.TradeRequested
        ? new[] { TalkTopic.ConvinceToStay, TalkTopic.PromiseTrade, TalkTopic.RefuseTrade, TalkTopic.AskWhatsWrong }
        : new[] { TalkTopic.AskWhatsWrong, TalkTopic.Praise, TalkTopic.Criticise, TalkTopic.DiscussRole, TalkTopic.PromiseMinutes };

    /// <summary>Why the coach can't talk to him right now, or null.</summary>
    public static string? Unavailable(Player p, DateOnly today) =>
        p.LastTalk is { } last && today.DayNumber - last.DayNumber < CooldownDays
            ? $"You spoke to him {today.DayNumber - last.DayNumber} day(s) ago; give it a week."
            : null;

    public static TalkResult Hold(Player p, Team team, TalkTopic topic, DateOnly today, double winPct, PlayerSeasonStats? stats,
                                  Finance finance, List<Promise> promises, Rng rng, Jobs.Coach? coach = null)
    {
        var h = p.Hidden;
        string name = p.FirstName;
        TalkResult r;
        switch (topic)
        {
            case TalkTopic.Praise:
            {
                bool playingWell = stats is { Games: >= 3 } && (stats.PlusMinus > 0 || stats.Ppg >= 12 || stats.Mpg >= PeopleEngine.ExpectedMinutes(p, team));
                r = playingWell
                    ? new($"{name} is pleased you noticed. \"Thanks, coach. I'll keep it going.\"", 6, true)
                    : new($"{name} shrugs it off. \"With respect, coach, I know I haven't been playing well.\"", -1, false);
                break;
            }
            case TalkTopic.Criticise:
                r = h.Professionalism >= 13
                    ? new($"{name} takes it on board. \"You're right. I'll put the work in.\"", 3, true)
                    : h.Temperament <= 8
                        ? new($"{name} reacts angrily and storms out.", -10, false)
                        : new($"{name} doesn't like it, but nods. \"Fine.\"", -3, false);
                break;
            case TalkTopic.PromiseMinutes:
            {
                if (p.DistrustUntil is { } d && d > today) { r = new($"{name} laughs. \"You said that last time.\"", -3, false); break; }
                if (promises.Any(x => x.PlayerId == p.Id)) { r = new($"{name}: \"You already promised me. Let's see it.\"", 0, false); break; }
                double now = stats?.Mpg ?? 0;
                double target = Math.Min(34, Math.Max(PeopleEngine.ExpectedMinutes(p, team), now + 5));
                promises.Add(new Promise { PlayerId = p.Id, TeamId = team.Id, TargetMpg = target, GamesAtStart = stats?.Games ?? 0, MinutesAtStart = stats?.Minutes ?? 0, Made = today });
                r = new($"{name} brightens up. \"I'll hold you to that.\" You've promised him about {target:0} minutes a game over his next 10 games. Set his minutes on the Rotation page.", 10, true);
                break;
            }
            case TalkTopic.DiscussRole:
            {
                var role = team.Tactic.RoleOf(p);
                double stars = Roles.Stars(p, role);
                var best = Roles.Best(p);
                r = stars >= 3.5
                    ? new($"{name} is comfortable as a {Roles.Info(role).Name}. \"That's my game.\"", 4, true)
                    : stars < 2.5
                        ? new($"{name} doesn't see himself as a {Roles.Info(role).Name}. \"I'm a {Roles.Info(best).Name}, coach.\"", -4, false)
                        : new($"{name} will do what's asked as a {Roles.Info(role).Name}, though he'd prefer {Roles.Info(best).Name}.", 1, true);
                break;
            }
            case TalkTopic.AskWhatsWrong:
            {
                var worst = PeopleEngine.Factors(p, team, winPct, stats, finance, today).Where(x => x.effect < 0).OrderBy(x => x.effect).Take(2).ToList();
                r = worst.Count == 0
                    ? new($"{name} says he's {PeopleEngine.Mood(p.Morale).ToLower()}. \"All good, coach.\"", 1, true)
                    : new($"{name} opens up: {string.Join("; ", worst.Select(x => x.reason.ToLower()))}.", 1, true);
                break;
            }
            case TalkTopic.PromiseTrade:
                if (p.DistrustUntil is { } dt && dt > today) { r = new($"{name}: \"I'll believe it when I see it.\"", -2, false); break; }
                promises.RemoveAll(x => x.PlayerId == p.Id);
                promises.Add(new Promise { PlayerId = p.Id, TeamId = team.Id, Made = today, Kind = "trade" });
                r = new($"{name} thanks you. He expects to be moved within 30 days.", 10, true);
                break;
            case TalkTopic.RefuseTrade:
                if (h.Ego >= 15) team.Chemistry = Math.Max(0, team.Chemistry - 5);
                r = new($"{name} is not happy. \"Then I'll make you regret it.\"" + (h.Ego >= 15 ? " The locker room has noticed his mood." : ""), -10, false);
                break;
            default: // ConvinceToStay
            {
                double chance = 0.2 + h.Loyalty / 40.0 + (winPct - 0.5) * 0.8 - (h.Ambition - 10) / 50.0;
                if (rng.Chance(Math.Clamp(chance, 0.05, 0.85)))
                {
                    p.TradeRequested = false; p.UnhappyWeeks = 0;
                    r = new($"After a long talk, {name} agrees to stay and withdraws his trade request.", 12, true);
                }
                else r = new($"{name} listens, but his mind is made up. He still wants out.", -5, false);
                break;
            }
        }
        // Man management: good talkers get more out of a good conversation and lose less in a bad one.
        if (coach != null) r = r with { MoraleChange = Math.Round(r.MoraleChange * coach.TalkMultiplier(r.MoraleChange >= 0), 1) };
        p.Morale = Math.Clamp(p.Morale + r.MoraleChange, 0, 100);
        // A conversation that lands lasts a few weeks rather than fading within days (critic review #3).
        if (r.Positive && r.MoraleChange >= 3) PeopleEngine.Boost(p, Math.Min(10, r.MoraleChange * 0.8), today.AddDays(28), "Encouraged by your talk", today);
        p.LastTalk = today;
        return r;
    }
}
