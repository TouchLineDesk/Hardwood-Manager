using Hardwood.Core.Model;
using Hardwood.Core.RealWorld;

namespace Hardwood.Core.Generation;

/// <summary>
/// The full names already in use by living players (D18, review #11 Top fix 3: 46 of 463 top-league players shared a full
/// name). A <see cref="PlayerGenerator"/> given a book re-rolls a new made-up player whose name is taken, from his own
/// heritage's lists, and adds the name it settles on. Names are compared like <see cref="RealNames.Key"/>: accent-,
/// case- and punctuation-blind. Built fresh from the world wherever players are generated, so it is never saved.
/// </summary>
public sealed class NameBook
{
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);

    public int Count => _used.Count;

    public static string Key(string first, string last) => RealNames.Key($"{first} {last}");

    public bool Taken(string first, string last) => _used.Contains(Key(first, last));

    /// <summary>Adds a name; false if it was already in the book.</summary>
    public bool Add(string first, string last) => _used.Add(Key(first, last));

    public static NameBook Of(params IEnumerable<Player>?[] groups)
    {
        var book = new NameBook();
        foreach (var g in groups)
            if (g != null)
                foreach (var p in g) book.Add(p.FirstName, p.LastName);
        return book;
    }

    /// <summary>Everyone alive in a career: the top league and its free agents, every club in the world, college players and
    /// high-school prospects, plus any extra list (a summer's free-agent pool).</summary>
    public static NameBook Of(World.GameWorld world, IEnumerable<Player>? extra = null) =>
        Of(world.Nabl.AllActivePlayers, world.Nabl.FreeAgents, world.Players, world.College?.Players, extra);

    /// <summary>
    /// Living made-up players (real NBA players excluded) and how many of them share their full name with another living
    /// player. The audit gates it; the CLI's <c>names</c> command prints it.
    /// </summary>
    public static (int MadeUp, int Sharing) Census(IEnumerable<Player> everyone)
    {
        var list = everyone.Distinct().ToList();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var p in list)
        {
            var k = Key(p.FirstName, p.LastName);
            counts[k] = counts.GetValueOrDefault(k) + 1;
        }
        int madeUp = 0, sharing = 0;
        foreach (var p in list)
        {
            if (RealNames.IsReal(p.FirstName, p.LastName)) continue;
            madeUp++;
            if (counts[Key(p.FirstName, p.LastName)] > 1) sharing++;
        }
        return (madeUp, sharing);
    }
}
