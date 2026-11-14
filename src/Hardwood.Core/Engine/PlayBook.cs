using Hardwood.Core.Model;

namespace Hardwood.Core.Engine;

/// <summary>One beat of a play: where the five stand (half-court feet: x 0..50 sideline to sideline, y 0..47 out from the
/// baseline; the basket at 25, 5.25), who has the ball, who is setting a screen (or -1), and a caption.</summary>
public sealed record PlayBeat(double[] X, double[] Y, int Ball, int Screener, string Caption);

/// <summary>
/// The Tactics page's play diagrams (Danny, #475, D60 A: "player floor movement"): each offensive system as a short
/// looping play for the five starters, PG to C, and where a defensive scheme puts its five against it. A picture of
/// what the system asks of the players, not a replay of the engine.
/// </summary>
public static class PlayBook
{
    public const double BasketX = 25, BasketY = 5.25;

    private static PlayBeat B(string caption, int ball, int screener, params (double x, double y)[] five) =>
        new(five.Select(p => p.x).ToArray(), five.Select(p => p.y).ToArray(), ball, screener, caption);

    public static IReadOnlyList<PlayBeat> For(OffensiveSystem s) => s switch
    {
        OffensiveSystem.PickAndRoll => new[]
        {
            B("Spread the floor around the handler", 0, -1, (25, 33), (45, 4), (5, 4), (8, 24), (33, 13)),
            B("The big comes up to set the ball screen", 0, 4, (24, 32), (45, 4), (5, 4), (8, 24), (27.5, 29.5)),
            B("Handler turns the corner, the big rolls", 0, -1, (19, 21), (46, 6), (4, 6), (8, 25), (27, 15)),
            B("Lob to the roller at the rim", 4, -1, (16, 14), (46, 6), (4, 6), (9, 24), (26, 7)),
        },
        OffensiveSystem.Motion => new[]
        {
            B("Five out, the ball at the top", 0, -1, (25, 30), (42, 22), (8, 22), (17, 17), (33, 17)),
            B("Pass to the wing, the guard cuts through", 1, -1, (25, 9), (42, 22), (9, 25), (13, 16), (33, 17)),
            B("Big flashes to the high post", 4, 3, (5, 4), (43, 21), (9, 26), (13, 16), (25, 21)),
            B("Backdoor cut for the layup", 2, -1, (5, 4), (44, 20), (19, 7), (12, 17), (25, 20)),
        },
        OffensiveSystem.PostCentric => new[]
        {
            B("The big posts up on the block", 0, -1, (30, 30), (44, 20), (6, 18), (14, 18), (34, 10)),
            B("Swing to the wing, he seals his man", 1, -1, (30, 30), (41, 19), (6, 18), (14, 19), (32, 8)),
            B("Entry pass: shooters space the floor", 4, -1, (28, 29), (46, 4), (4, 6), (12, 22), (31, 8)),
            B("Drop step to the rim", 4, -1, (28, 29), (46, 4), (4, 6), (12, 22), (27, 5.5)),
        },
        OffensiveSystem.PaceAndSpace => new[]
        {
            B("Push it up the floor", 0, -1, (25, 42), (46, 32), (4, 32), (14, 38), (31, 44)),
            B("Shooters sprint to the corners", 0, -1, (25, 28), (47, 3), (3, 3), (9, 25), (39, 25)),
            B("Drive and draw the help", 0, -1, (23, 13), (47, 3), (3, 3), (9, 25), (39, 25)),
            B("Kick out for the corner three", 1, -1, (22, 14), (47, 3), (3, 3), (9, 25), (39, 25)),
        },
        OffensiveSystem.Isolation => new[]
        {
            B("The star gets the ball on the wing", 1, -1, (24, 30), (34, 27), (46, 4), (4, 6), (40, 14)),
            B("Everyone clears to the other side", 1, -1, (8, 26), (33, 26), (4, 4), (10, 12), (17, 8)),
            B("He sizes his man up", 1, -1, (8, 26), (35, 22), (4, 4), (10, 12), (17, 8)),
            B("Pull-up jumper", 1, -1, (8, 26), (36, 16), (4, 4), (10, 12), (17, 8)),
        },
        _ => new[]
        {
            B("Ball at the top, bigs at the elbows", 0, -1, (25, 30), (42, 22), (8, 22), (18, 17), (32, 17)),
            B("Pass to the wing", 2, -1, (21, 28), (42, 22), (8, 22), (18, 17), (32, 12)),
            B("Baseline drive, the big slides down", 2, -1, (21, 28), (43, 22), (10, 9), (18, 20), (28, 6.5)),
            B("Dump-off at the rim", 4, -1, (21, 28), (43, 22), (10, 9), (18, 20), (27, 6)),
        },
    };

    /// <summary>Where a scheme's five stand against the offence at a moment (the ball at <paramref name="ballX"/>).</summary>
    public static (double x, double y)[] Defence(DefensiveScheme d, double[] ox, double[] oy, double ballX, double ballY)
    {
        var r = new (double x, double y)[5];
        if (d == DefensiveScheme.Zone23)
        {
            double shift = (ballX - 25) * 0.3;
            (double, double)[] spots = { (19, 21), (31, 21), (10, 10), (25, 7.5), (40, 10) };
            for (int i = 0; i < 5; i++) r[i] = (spots[i].Item1 + shift * (i == 3 ? 0.5 : 1), spots[i].Item2 + (ballY < 15 ? -2 : 0));
            return r;
        }
        for (int i = 0; i < 5; i++)
        {
            double k = i == 4 && d == DefensiveScheme.Drop ? 0.55 : 0.25; // drop: the big sags to protect the rim
            r[i] = (ox[i] + (BasketX - ox[i]) * k, oy[i] + (BasketY - oy[i]) * k);
        }
        return r;
    }

    /// <summary>The scheme's note under the diagram.</summary>
    public static string DefenceNote(DefensiveScheme d) => d switch
    {
        DefensiveScheme.Zone23 => "2-3 zone: two up top, three across the paint, shifting with the ball.",
        DefensiveScheme.Drop => "Drop coverage: the big sags to the rim; the guards fight over screens.",
        DefensiveScheme.SwitchEverything => "Switch everything: whoever is nearest takes the man; no help needed.",
        _ => "Man-to-man: each defender a step off his man, between him and the basket.",
    };
}
