using SimConsole;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.CursorVisible = false;
Console.Clear();

var world = WorldSeeder.CreateDemo();
var renderer = new Renderer();

float[] speeds = [0.25f, 0.5f, 1f, 2f, 4f, 8f, 16f];
var speedIndex = 2; // start at 1 tick/sec
var paused = false;
var lastTick = DateTime.UtcNow;
var lastRenderedTick = -1;
var lastPaused = true; // force initial render

try
{
    while (true)
    {
        while (Console.KeyAvailable)
        {
            var key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.Q or ConsoleKey.Escape:
                    return;
                case ConsoleKey.Spacebar:
                    paused = !paused;
                    break;
                case ConsoleKey.RightArrow or ConsoleKey.Add or ConsoleKey.OemPlus:
                    speedIndex = Math.Min(speedIndex + 1, speeds.Length - 1);
                    break;
                case ConsoleKey.LeftArrow or ConsoleKey.Subtract or ConsoleKey.OemMinus:
                    speedIndex = Math.Max(speedIndex - 1, 0);
                    break;
                case ConsoleKey.D:
                    // trigger disease outbreak on the most populous tile
                    var target = world.State.Map.AllTiles()
                        .OrderByDescending(t => t.Populations.Sum(p => p.Count))
                        .FirstOrDefault();
                    if (target is not null)
                        world.Apply(new EcosystemSim.TriggerDiseaseCommand
                        {
                            Disease = WorldSeeder.DinoFever,
                            TileX = target.X,
                            TileY = target.Y
                        });
                    break;
            }
        }

        if (!paused && DateTime.UtcNow - lastTick >= TimeSpan.FromSeconds(1.0 / speeds[speedIndex]))
        {
            world.Tick();
            lastTick = DateTime.UtcNow;
        }

        if (world.State.Tick != lastRenderedTick || paused != lastPaused)
        {
            renderer.Render(world, speeds[speedIndex], paused);
            lastRenderedTick = world.State.Tick;
            lastPaused = paused;
        }

        Thread.Sleep(16); // 60hz input polling, renders only on state change
    }
}
finally
{
    Console.CursorVisible = true;
    Console.ResetColor();
    Console.Clear();
}
