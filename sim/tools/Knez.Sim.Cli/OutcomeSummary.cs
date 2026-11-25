namespace Knez.Sim.Cli;

/// <summary>
/// The D-026 measures over a batch of runs (battle-sim.md §11, M3 step 7): win rate per side, how battles ended, battle
/// length, losses when a squad broke, the fallen per side, blows that got through. Keeps every run, so each measure has
/// its per-run values for a standard error. <c>batch</c> prints it; <c>BalanceReferenceTests</c> pins it.
/// </summary>
public sealed class OutcomeSummary
{
    private readonly List<OutcomeMeter> _runs = [];

    /// <summary>Runs added.</summary>
    public int Runs => _runs.Count;

    /// <summary>Sides that had men in any run, in order.</summary>
    public IReadOnlyList<int> Sides => Enumerable.Range(0, Defs.SquadSpawnDef.MaxSideId + 1)
        .Where(side => _runs.Any(r => r.StartOf(side) > 0)).ToArray();

    /// <summary>Blows struck up to the decision, all runs.</summary>
    public long Blows => _runs.Sum(r => (long)r.Blows);

    /// <summary>Blows that got through, all runs.</summary>
    public long Landed => _runs.Sum(r => (long)r.Landed);

    /// <summary>Share of all blows that got through, 0..1.</summary>
    public double LandedRate => Blows == 0 ? 0 : Landed / (double)Blows;

    /// <summary>Standard error of <see cref="LandedRate"/>, with runs as the samples (blows of one run aren't
    /// independent) and weighted by their blows: the ratio estimator's √(Σ(landedᵢ − R·blowsᵢ)² / (n(n−1))) / mean blows.</summary>
    public double LandedRateError
    {
        get
        {
            int n = _runs.Count;
            if (n < 2 || Blows == 0)
            {
                return 0;
            }

            double rate = LandedRate;
            double sum = _runs.Sum(r => (r.Landed - (rate * r.Blows)) * (r.Landed - (rate * r.Blows)));
            return Math.Sqrt(sum / (n * (double)(n - 1))) / (Blows / (double)n);
        }
    }

    /// <summary>Seconds from the start to the decision, decided runs only.</summary>
    public IReadOnlyList<double> EndSeconds => _runs.Where(r => r.Decided).Select(r => r.EndTick / (double)BattleState.TicksPerSecond).ToArray();

    /// <summary>Seconds from the first blow to the decision, decided runs with a blow only.</summary>
    public IReadOnlyList<double> FightSeconds => _runs.Where(r => r.Decided && r.FirstBlowTick >= 0)
        .Select(r => (r.EndTick - r.FirstBlowTick) / (double)BattleState.TicksPerSecond).ToArray();

    /// <summary>Every squad's first break, all runs: the share of its starting men down then, 0..1.</summary>
    public IReadOnlyList<double> LossAtBreak => _runs.SelectMany(r => r.LossAtBreak).ToArray();

    /// <summary>The squads of one side that broke, all runs: the share of its starting men each had down then.</summary>
    public IReadOnlyList<double> LossAtBreakOf(int side) => _runs.SelectMany(r => r.LossAtBreak.Where((_, i) => r.LossAtBreakSides[i] == side)).ToArray();

    /// <summary>Adds one run.</summary>
    public void Add(OutcomeMeter run) => _runs.Add(run);

    /// <summary>Share of runs a side won, 0..1.</summary>
    public double WinRate(int side) => Runs == 0 ? 0 : _runs.Count(r => r.Winner == side) / (double)Runs;

    /// <summary>Runs that ended this way.</summary>
    public int EndCount(BattleEnd end) => _runs.Count(r => r.End == end);

    /// <summary>Share of runs that ended this way, 0..1.</summary>
    public double EndRate(BattleEnd end) => Runs == 0 ? 0 : EndCount(end) / (double)Runs;

    /// <summary>Per run, the share of a side's men down at the decision (or the time limit), 0..1.</summary>
    public IReadOnlyList<double> FallenShares(int side) => _runs.Where(r => r.StartOf(side) > 0)
        .Select(r => r.FallenOf(side) / (double)r.StartOf(side)).ToArray();

    /// <summary>Mean share of a side's men down at the decision (or the time limit), 0..1.</summary>
    public double FallenShare(int side) => Mean(FallenShares(side));

    /// <summary>Mean of a list, 0 when empty.</summary>
    public static double Mean(IReadOnlyList<double> values) => values.Count == 0 ? 0 : values.Average();

    /// <summary>Standard error of the mean of a list (sample deviation / √n), 0 under two values.</summary>
    public static double StandardError(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return 0;
        }

        double mean = values.Average();
        double variance = values.Sum(v => (v - mean) * (v - mean)) / (values.Count - 1);
        return Math.Sqrt(variance / values.Count);
    }

    /// <summary>Standard error of a share p seen in n runs, √(p(1−p)/n), with p kept at least 3/n from 0 and 1 (the
    /// "rule of three"): a share seen 0 times in n runs is still uncertain by about that much, not by zero.</summary>
    public static double RateError(double p, int n)
    {
        if (n == 0)
        {
            return 0;
        }

        double edge = Math.Min(0.5, 3.0 / n);
        double q = Math.Clamp(p, edge, 1 - edge);
        return Math.Sqrt(q * (1 - q) / n);
    }

    /// <summary>The q-quantile (0..1) of a list, nearest rank; 0 when empty.</summary>
    public static double Quantile(IReadOnlyList<double> values, double q)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sorted = values.Order().ToArray();
        return sorted[Math.Clamp((int)Math.Ceiling(q * sorted.Length) - 1, 0, sorted.Length - 1)];
    }
}
