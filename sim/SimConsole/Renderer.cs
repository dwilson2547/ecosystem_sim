using EcosystemSim;

namespace SimConsole;

public class Renderer
{
    private static readonly ConsoleColor[] Palette =
    [
        ConsoleColor.Cyan, ConsoleColor.Green, ConsoleColor.Yellow,
        ConsoleColor.Magenta, ConsoleColor.Blue, ConsoleColor.Red
    ];

    private readonly Dictionary<string, ConsoleColor> _speciesColors = new();
    private readonly Dictionary<Population, int> _prevCounts = new();
    private int _paletteIndex;

    public void Render(World world, float speed, bool paused)
    {
        Console.SetCursorPosition(0, 0);

        RenderHeader(world.State.Tick, speed, paused);
        Console.WriteLine();
        RenderMap(world.State.Map);
        Console.WriteLine();
        RenderPopulations(world.State.Map);
        Console.WriteLine();
        RenderFactionRelations(world.State.Factions);

        foreach (var pop in world.State.Map.AllPopulations())
            _prevCounts[pop] = pop.Count;
    }

    private static void RenderHeader(int tick, float speed, bool paused)
    {
        Write("EcosystemSim", ConsoleColor.White);
        Write($"  Tick: {tick,6}", ConsoleColor.DarkGray);
        Write($"  Speed: {speed}x");

        if (paused)
            Write("  [PAUSED]", ConsoleColor.Yellow);

        WriteLine("  [SPACE] pause  [← →] speed  [D] disease  [T] trade  [Q] quit", ConsoleColor.DarkGray);
    }

    private void RenderMap(WorldMap map)
    {
        WriteLine("WORLD MAP  (food: ·=empty  ░=low  ▒=med  ▓=high  █=full | letter=species | ■=fertile)");
        Console.Write("╔" + new string('═', map.Width) + "╗");
        Console.WriteLine();

        for (var y = 0; y < map.Height; y++)
        {
            Console.Write("║");
            for (var x = 0; x < map.Width; x++)
                RenderCell(map.GetTile(x, y));
            Console.ResetColor();
            Console.WriteLine("║");
        }

        Console.WriteLine("╚" + new string('═', map.Width) + "╝");
    }

    private void RenderCell(Tile tile)
    {
        var fertilizer = tile.Byproducts.FirstOrDefault(b => b.Type == EcosystemSim.ByproductType.Fertilizer);
        if (fertilizer?.Amount > 30f)
            Console.BackgroundColor = ConsoleColor.DarkGreen;

        var dominant = tile.Populations
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .FirstOrDefault();

        if (dominant is not null)
        {
            // infected populations render in dark red regardless of species color
            Console.ForegroundColor = dominant.Disease is not null
                ? ConsoleColor.DarkRed
                : GetColor(dominant.Species);
            Console.Write(dominant.Species.Name[0]);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            var food = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Food);
            Console.Write(FoodChar(food));
        }

        Console.ResetColor();
    }

    private static char FoodChar(ResourcePool? pool)
    {
        if (pool is null || pool.Capacity == 0) return '·';
        return (pool.Amount / pool.Capacity) switch
        {
            0 => '·',
            <= 0.25f => '░',
            <= 0.5f => '▒',
            <= 0.75f => '▓',
            _ => '█'
        };
    }

    private void RenderPopulations(WorldMap map)
    {
        WriteLine($"{"SPECIES",-20} {"FACTION",-18} {"TILE",-7} {"COUNT",6}  {"TREND",5}  SAT   FERT   SIZE   DISEASE");
        WriteLine(new string('─', 91), ConsoleColor.DarkGray);

        var populations = map.AllPopulations()
            .OrderByDescending(p => p.Count) // extinct (Count=0) naturally fall to the bottom
            .ToList();

        if (populations.All(p => p.Count == 0))
        {
            WriteLine("  All species extinct.", ConsoleColor.Red);
            return;
        }

        foreach (var pop in populations)
        {
            var tile = pop.CurrentTile ?? map.AllTiles().First(t => t.Populations.Contains(pop));
            var (trend, trendColor) = Trend(pop);
            var satisfaction = (int)(pop.LastSatisfaction * 100);
            var satColor = satisfaction >= 90 ? ConsoleColor.Green
                : satisfaction >= 50 ? ConsoleColor.Yellow
                : ConsoleColor.Red;
            var factionName = pop.Faction?.Name ?? "—";

            Write($"{pop.Species.Name,-20}", GetColor(pop.Species));
            Write($" {factionName,-18}");
            Write($" ({tile.X},{tile.Y})  ");
            Write($"{pop.Count,6}  ");

            if (pop.Count == 0)
            {
                Write("  [EXTINCT]", ConsoleColor.DarkGray);
                Console.WriteLine();
                continue;
            }

            Write($"{trend,5}  ", trendColor);
            Write($"{satisfaction,3}%", satColor);

            var fertAmount = (int)(pop.CurrentTile?.Byproducts
                .FirstOrDefault(b => b.Type == EcosystemSim.ByproductType.Fertilizer)?.Amount ?? 0f);
            var fertColor = fertAmount > 60 ? ConsoleColor.Green
                : fertAmount > 20 ? ConsoleColor.DarkGreen
                : ConsoleColor.DarkGray;
            Write($"  {fertAmount,4}", fertColor);

            var sizeColor = pop.SizeIndex > 1.02f ? ConsoleColor.Green
                : pop.SizeIndex < 0.98f ? ConsoleColor.Red
                : ConsoleColor.DarkGray;
            Write($"  {pop.SizeIndex:F2}x", sizeColor);

            if (pop.Disease is not null)
            {
                var pct = (int)(pop.InfectionLevel * 100);
                Write($"  [{pop.Disease.Name} {pct}%]", ConsoleColor.DarkRed);
            }

            Console.WriteLine();
        }
    }

    private static void RenderFactionRelations(List<Faction> factions)
    {
        WriteLine("FACTION RELATIONS");
        WriteLine(new string('─', 68), ConsoleColor.DarkGray);

        var seen = new HashSet<(Faction, Faction)>();
        var anyRelations = false;

        foreach (var faction in factions.Where(f => !f.IsExtinct))
        {
            foreach (var (other, relation) in faction.Relations)
            {
                if (seen.Contains((other, faction))) continue;
                seen.Add((faction, other));
                anyRelations = true;

                Write($"  {faction.Name,-22}");
                Write(" ←→ ", ConsoleColor.DarkGray);
                Write($"{other.Name,-22}");
                Write("  ");

                var (stateLabel, stateColor) = relation.State switch
                {
                    DiplomaticState.Allied  => ("ALLIED",  ConsoleColor.Green),
                    DiplomaticState.Neutral => ("NEUTRAL", ConsoleColor.DarkGray),
                    DiplomaticState.Tense   => ("TENSE",   ConsoleColor.Yellow),
                    DiplomaticState.AtWar   => ("AT WAR",  ConsoleColor.Red),
                    _                       => ("?",       ConsoleColor.DarkGray)
                };

                Write($"{stateLabel,-8}", stateColor);

                var score = relation.TensionScore;
                var scoreColor = score > 0 ? ConsoleColor.Red : score < 0 ? ConsoleColor.Green : ConsoleColor.DarkGray;
                var warNote = relation.State == DiplomaticState.AtWar && relation.TicksAtWar > 0
                    ? $"  [{relation.TicksAtWar} ticks at war]"
                    : string.Empty;
                Write($"  {score:+0.00;-0.00;0.00}{warNote}", scoreColor);

                if (relation.HasTradeAgreement)
                    Write("  [TRADE]", ConsoleColor.Cyan);

                Console.WriteLine();
            }
        }

        if (!anyRelations)
            WriteLine("  No factions in proximity yet.", ConsoleColor.DarkGray);
    }

    private (string symbol, ConsoleColor color) Trend(Population pop)
    {
        if (!_prevCounts.TryGetValue(pop, out var prev))
            return ("·", ConsoleColor.DarkGray);
        return pop.Count.CompareTo(prev) switch
        {
            > 0 => ("↑", ConsoleColor.Green),
            < 0 => ("↓", ConsoleColor.Red),
            _ => ("→", ConsoleColor.DarkGray)
        };
    }

    private ConsoleColor GetColor(SpeciesDefinition species)
    {
        if (_speciesColors.TryGetValue(species.Name, out var color)) return color;
        color = Palette[_paletteIndex++ % Palette.Length];
        _speciesColors[species.Name] = color;
        return color;
    }

    private static void Write(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ResetColor();
    }

    private static void WriteLine(string text, ConsoleColor color = ConsoleColor.Gray)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ResetColor();
    }
}
