using Hardwood.Core.Model;

namespace Hardwood.Tests;

/// <summary>
/// The NABL's rules before the open market (decision #80): soft cap, Bird rights, draft and pick trading. The code still
/// supports them, so their tests run with this switched on.
/// </summary>
public static class Legacy
{
    public static League Rules(League league)
    {
        league.Finance = league.Finance with { OpenMarket = false };
        league.EnsurePicks();
        return league;
    }
}
