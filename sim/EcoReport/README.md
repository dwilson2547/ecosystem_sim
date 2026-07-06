# EcoReport

Headless ecology stability report for the demo world. Ticks the simulation with no UI, repeats it
across several independent runs, and prints per-lineage metrics so "is this assemblage balanced?"
is a number instead of a vibe. Built for balance tuning: change a rate, re-run, compare.

```bash
cd sim
dotnet run --project EcoReport -c Release                 # 5 runs × 600 ticks, unseeded (random)
dotnet run --project EcoReport -c Release -- --runs 10 --ticks 1000
dotnet run --project EcoReport -c Release -- --seed 42    # reproducible batch — hold RNG fixed to A/B a param
dotnet run --project EcoReport -c Release -- --trace 50   # population line every 50 ticks (run 1)
dotnet run --project EcoReport -c Release -- --csv out.csv # dump run/tick/lineage counts
```

## Why multiple runs

Unseeded, every run diverges (the engine RNG is time-based). One run can't tell a robust outcome from
a fluke — e.g. Parasaurolophus goes extinct in ~1 of 5 runs, so a single run over- or under-states the
risk. Aggregating across runs shows the real distribution; bumping `--runs` sharpens the signal.

Pass `--seed N` to make a batch **reproducible**: run *r* uses seed `N+r`, so runs still diverge from
each other (the aggregate still spans the outcome distribution) but the whole batch repeats exactly.
That's what lets you hold the RNG fixed, change one balance parameter, and attribute the difference to
the change rather than to luck. Seeding threads through both the tick RNG and the resource-pool RNG.

## Reading the table

| Column | Meaning |
|--------|---------|
| Start / Final | initial vs. end-of-run head-count (Final is mean±sd across runs) |
| Trough / Peak | mean per-run minimum / maximum — how low it dips, how high it spikes |
| Osc | mean coefficient of variation over the run's second half; high = boom/bust oscillation |
| Extinct | fraction of runs where the lineage hit 0 after being alive, at the mean tick it happened |
| Flag | `stable`, `EXTINCT`, `BOOM` (peaks ≥5× start), or `swingy` (survives but CV ≥60%) |

Lineages are keyed by species **root name**, so speciation offshoots (e.g. "Lesser Tyrannosaurus")
fold back into their lineage and a rename isn't miscounted as a death. The report is species-agnostic
— it discovers whatever lineages appear, so species added later show up with no change to this tool.

## Known limits / next steps

- Reports populations only. Environmental series (weather, resource levels) and interaction rates
  (hunt success, herd-defense deterrence) aren't tracked yet — add columns here as those systems land.
