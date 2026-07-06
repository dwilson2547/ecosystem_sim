using EcosystemSim;
using SimConsole;

// Headless ecology report. Ticks the demo world for a fixed span, repeats it across several
// independent runs (the engine RNG is unseeded, so each run diverges — repetition is how we tell a
// robust outcome from a fluke), and prints per-lineage stability metrics. Species-agnostic: it
// discovers whatever lineages appear, so new species / weather / insects show up automatically with
// no code change here. Speciation offshoots are folded back to their lineage root so a rename isn't
// counted as an extinction.

var opts = Options.Parse(args);
if (opts is null) return 0;   // --help already printed

Console.WriteLine($"Ecology report — {opts.Runs} run(s) × {opts.Ticks} ticks"
                + (opts.Seed is { } sv ? $", seeded {sv}..{sv + opts.Runs - 1}" : ", unseeded")
                + (opts.TraceInterval > 0 ? $", trace every {opts.TraceInterval}" : ""));
Console.WriteLine(new string('─', 78));

// series[lineage][tick] = total head-count of that lineage at that tick, one dictionary per run.
// A seeded batch gives run r the seed (base + r): the whole batch is reproducible, yet runs still
// diverge from each other so the aggregate still spans the outcome distribution. Unseeded → all fresh.
var runs = new List<Dictionary<string, int[]>>();
for (var r = 0; r < opts.Runs; r++)
    runs.Add(RunOnce(opts.Ticks, r == 0 ? opts.TraceInterval : 0, opts.Seed is { } b ? b + r : null));

var lineages = runs.SelectMany(s => s.Keys).Distinct().OrderBy(n => n).ToList();
var stats = lineages.ToDictionary(l => l, l => LineageStats.From(l, runs, opts.Ticks));

PrintTable(stats.Values);
PrintVerdict(stats.Values, opts.Runs);
if (opts.CsvPath is not null) WriteCsv(opts.CsvPath, runs, opts.Ticks);
return 0;

// ── one simulation run ─────────────────────────────────────────────────────────────────────────
// Records every tick internally (needed for accurate troughs / oscillation); only echoes a trace
// line every `traceInterval` ticks when asked.
Dictionary<string, int[]> RunOnce(int ticks, int traceInterval, int? seed)
{
    var world = WorldSeeder.CreateDemo(seed);
    var series = new Dictionary<string, int[]>();

    for (var t = 0; t <= ticks; t++)
    {
        if (t > 0) world.Tick();

        foreach (var g in world.State.Map.AllTiles()
                     .SelectMany(tile => tile.Populations)
                     .Where(p => p.Count > 0)
                     .GroupBy(p => p.Species.EffectiveRootName))
        {
            if (!series.TryGetValue(g.Key, out var arr))
                series[g.Key] = arr = new int[ticks + 1];
            arr[t] = g.Sum(p => p.Count);
        }

        if (traceInterval > 0 && t % traceInterval == 0)
            Console.WriteLine($"  t={t,4}  " + string.Join("  ",
                series.Where(kv => kv.Value[t] > 0)
                      .OrderBy(kv => kv.Key)
                      .Select(kv => $"{kv.Key}={kv.Value[t]}")));
    }
    if (traceInterval > 0) Console.WriteLine(new string('─', 78));
    return series;
}

void PrintTable(IEnumerable<LineageStats> rows)
{
    Console.WriteLine($"{"Lineage",-18} {"Start",5} {"Final",13} {"Trough",7} {"Peak",6} {"Osc",5}  {"Extinct",-14} Flag");
    foreach (var s in rows.OrderByDescending(s => s.Start))
        Console.WriteLine(
            $"{s.Lineage,-18} {s.Start,5} {s.FinalMean,6:0.#}±{s.FinalSd,-6:0.#} {s.TroughMean,7:0.#} "
          + $"{s.PeakMean,6:0.#} {s.OscCvPct,4:0}% {s.ExtinctLabel,-14} {s.Flag}");
    Console.WriteLine(new string('─', 78));
    Console.WriteLine("Final = mean±sd across runs · Trough/Peak = mean per-run min/max · "
                    + "Osc = mean CV% over 2nd half · Extinct = runs hitting 0 (@ mean tick)");
}

void PrintVerdict(IEnumerable<LineageStats> rows, int runCount)
{
    Console.WriteLine();
    Console.WriteLine("Verdict");
    var problems = rows.Where(s => s.Flag is not "stable").OrderBy(s => s.Flag).ToList();
    if (problems.Count == 0) { Console.WriteLine("  all lineages stable across every run."); return; }
    foreach (var s in problems)
        Console.WriteLine(s.Flag switch
        {
            "EXTINCT" => $"  ✗ {s.Lineage}: extinct in {s.ExtinctLabel}.",
            "BOOM"    => $"  ▲ {s.Lineage}: unchecked — peaks ~{s.PeakMean:0} (~{s.PeakMean / s.Start:0}× start {s.Start}).",
            "swingy"  => $"  ~ {s.Lineage}: survives but oscillates hard ({s.OscCvPct:0}% CV).",
            _         => $"  · {s.Lineage}: {s.Flag}.",
        });
}

void WriteCsv(string path, List<Dictionary<string, int[]>> allRuns, int ticks)
{
    using var w = new StreamWriter(path);
    w.WriteLine("run,tick,lineage,count");
    for (var r = 0; r < allRuns.Count; r++)
        foreach (var (lineage, arr) in allRuns[r])
            for (var t = 0; t <= ticks; t++)
                if (arr[t] > 0) w.WriteLine($"{r},{t},{lineage},{arr[t]}");
    Console.WriteLine($"\nwrote {path}");
}

// ── metrics ────────────────────────────────────────────────────────────────────────────────────

// Per-lineage aggregate across all runs. A lineage counts as "extinct" in a run only if it was ever
// alive and then hit 0 — so a species that simply never appears isn't flagged, and a speciation
// rename (folded into its root) doesn't read as a death.
sealed record LineageStats(
    string Lineage, int Start, double FinalMean, double FinalSd, double TroughMean, double PeakMean,
    double OscCvPct, int ExtinctRuns, int RunCount, double MeanExtinctTick)
{
    public string ExtinctLabel => ExtinctRuns == 0 ? "—" : $"{ExtinctRuns}/{RunCount} @t{MeanExtinctTick:0}";

    public string Flag =>
        ExtinctRuns > 0                     ? "EXTINCT"
      : Start > 0 && PeakMean >= 5 * Start  ? "BOOM"
      : OscCvPct >= 60                      ? "swingy"
      :                                       "stable";

    public static LineageStats From(string lineage, List<Dictionary<string, int[]>> runs, int ticks)
    {
        var finals = new List<double>();
        var troughs = new List<double>();
        var peaks = new List<double>();
        var cvs = new List<double>();
        var extinctTicks = new List<int>();

        foreach (var run in runs)
        {
            if (!run.TryGetValue(lineage, out var arr)) continue;   // never appeared in this run
            finals.Add(arr[ticks]);
            troughs.Add(arr.Min());
            peaks.Add(arr.Max());
            cvs.Add(Cv(arr.Skip(ticks / 2)));

            var everAlive = false;
            for (var t = 0; t <= ticks; t++)
            {
                if (arr[t] > 0) everAlive = true;
                else if (everAlive) { extinctTicks.Add(t); break; }
            }
        }

        return new LineageStats(
            lineage,
            Start: runs[0].TryGetValue(lineage, out var a0) ? a0[0] : 0,
            FinalMean: Mean(finals), FinalSd: Sd(finals),
            TroughMean: Mean(troughs), PeakMean: Mean(peaks),
            OscCvPct: Mean(cvs) * 100,
            ExtinctRuns: extinctTicks.Count, RunCount: runs.Count,
            MeanExtinctTick: Mean(extinctTicks.Select(t => (double)t)));
    }

    static double Mean(IEnumerable<double> xs) { var l = xs.ToList(); return l.Count == 0 ? 0 : l.Average(); }
    static double Sd(IReadOnlyCollection<double> xs)
    {
        if (xs.Count < 2) return 0;
        var m = xs.Average();
        return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / xs.Count);
    }
    static double Cv(IEnumerable<int> xs)
    {
        var l = xs.Select(x => (double)x).ToList();
        var m = l.Average();
        return m <= 0 ? 0 : Sd(l) / m;
    }
}

// ── options ────────────────────────────────────────────────────────────────────────────────────
sealed record Options(int Ticks, int Runs, int TraceInterval, string? CsvPath, int? Seed)
{
    public static Options? Parse(string[] args)
    {
        int ticks = 600, runs = 5, trace = 0;
        int? seed = null;
        string? csv = null;
        for (var i = 0; i < args.Length; i++)
            switch (args[i])
            {
                case "-h" or "--help":
                    Console.WriteLine(
                        "EcoReport — headless ecology stability report for the demo world\n\n"
                      + "  --ticks N    ticks per run (default 600)\n"
                      + "  --runs N     independent runs to aggregate (default 5)\n"
                      + "  --seed N     reproducible batch: run r uses seed N+r (default: unseeded/random)\n"
                      + "  --trace N    print a population line every N ticks of run 1 (default off)\n"
                      + "  --csv PATH   dump per-run/tick/lineage counts to CSV\n");
                    return null;
                case "--ticks": ticks = int.Parse(args[++i]); break;
                case "--runs":  runs  = int.Parse(args[++i]); break;
                case "--seed":  seed  = int.Parse(args[++i]); break;
                case "--trace": trace = int.Parse(args[++i]); break;
                case "--csv":   csv   = args[++i]; break;
                default: Console.Error.WriteLine($"unknown arg: {args[i]}"); return null;
            }
        return new Options(ticks, runs, trace, csv, seed);
    }
}
