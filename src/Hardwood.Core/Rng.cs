namespace Hardwood.Core;

/// <summary>
/// Seeded random source (xoshiro256**). Every sim is reproducible from its seed,
/// so a bug report can carry a seed and replay the exact game.
/// </summary>
public sealed class Rng
{
    private ulong _s0, _s1, _s2, _s3;

    public ulong Seed { get; }

    public Rng(ulong seed)
    {
        Seed = seed;
        // SplitMix64 to expand the seed.
        ulong x = seed;
        _s0 = Next(ref x); _s1 = Next(ref x); _s2 = Next(ref x); _s3 = Next(ref x);

        static ulong Next(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextULong()
    {
        ulong result = Rotl(_s1 * 5, 7) * 9;
        ulong t = _s1 << 17;
        _s2 ^= _s0; _s3 ^= _s1; _s1 ^= _s2; _s0 ^= _s3;
        _s2 ^= t;
        _s3 = Rotl(_s3, 45);
        return result;
    }

    /// <summary>Uniform double in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    /// <summary>Uniform int in [min, max).</summary>
    public int Next(int min, int max) => min + (int)(NextDouble() * (max - min));

    public int Next(int max) => Next(0, max);

    public bool Chance(double p) => NextDouble() < p;

    /// <summary>Standard normal via Box–Muller.</summary>
    public double NextGaussian()
    {
        double u1 = 1.0 - NextDouble();
        double u2 = NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public double NextGaussian(double mean, double stdDev) => mean + stdDev * NextGaussian();

    /// <summary>Pick an index from a list of non-negative weights.</summary>
    public int WeightedIndex(ReadOnlySpan<double> weights)
    {
        double total = 0;
        foreach (var w in weights) total += w;
        if (total <= 0) return Next(weights.Length);
        double r = NextDouble() * total;
        for (int i = 0; i < weights.Length; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        return weights.Length - 1;
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Next(items.Count)];

    public void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
