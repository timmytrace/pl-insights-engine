namespace Studio.Engine;

/// <summary>Seeded random source with the distributions the simulator needs.</summary>
public sealed class Rng(int seed)
{
    private readonly Random _random = new(seed);

    public double Next() => _random.NextDouble();

    public double Uniform(double lo, double hi) => lo + (hi - lo) * _random.NextDouble();

    public double Gauss(double mean, double sd)
    {
        // Box-Muller; 1 - u keeps the log argument away from zero.
        var u1 = 1.0 - _random.NextDouble();
        var u2 = _random.NextDouble();
        return mean + sd * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public T Choice<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    public bool Chance(double p) => _random.NextDouble() < p;

    public int Weighted(IReadOnlyList<double> weights)
    {
        var total = weights.Sum();
        var r = _random.NextDouble() * total;
        for (var i = 0; i < weights.Count; i++)
        {
            r -= weights[i];
            if (r < 0) return i;
        }
        return weights.Count - 1;
    }

    public List<T> Sample<T>(IReadOnlyList<T> items, int count)
    {
        var pool = items.ToList();
        var result = new List<T>(count);
        for (var i = 0; i < count; i++)
        {
            var j = _random.Next(pool.Count);
            result.Add(pool[j]);
            pool.RemoveAt(j);
        }
        return result;
    }
}
