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

        WriteLine("  [SPACE] pause  [← →] speed  [Q] quit", ConsoleColor.DarkGray);
    }

    private void RenderMap(WorldMap map)
    {
        WriteLine("WORLD MAP  (food: ·=empty  ░=low  ▒=med  ▓=high  █=full | letter=species)");
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
        var dominant = tile.Populations
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .FirstOrDefault();

        if (dominant is not null)
        {
            Console.ForegroundColor = GetColor(dominant.Species);
            Console.Write(dominant.Species.Name[0]);
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            var food = tile.Resources.FirstOrDefault(r => r.Type == ResourceType.Food);
            Console.Write(FoodChar(food));
        }
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
        WriteLine($"{"SPECIES",-22} {"TILE",-7} {"COUNT",6}  {"TREND",5}  SATISFACTION");
        WriteLine(new string('─', 58), ConsoleColor.DarkGray);

        var populations = map.AllPopulations()
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count)
            .ToList();

        if (populations.Count == 0)
        {
            WriteLine("  All species extinct.", ConsoleColor.Red);
            return;
        }

        foreach (var pop in populations)
        {
            var tile = map.AllTiles().First(t => t.Populations.Contains(pop));
            var (trend, trendColor) = Trend(pop);
            var satisfaction = (int)(pop.LastSatisfaction * 100);
            var satColor = satisfaction >= 90 ? ConsoleColor.Green
                : satisfaction >= 50 ? ConsoleColor.Yellow
                : ConsoleColor.Red;

            Write($"{pop.Species.Name,-22}", GetColor(pop.Species));
            Write($" ({tile.X},{tile.Y})  ");
            Write($"{pop.Count,6}  ");
            Write($"{trend,5}  ", trendColor);
            WriteLine($"{satisfaction,10}%", satColor);
        }
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
