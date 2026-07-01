namespace EcosystemSim;

public class WorldMap
{
    public int Width { get; }
    public int Height { get; }

    private readonly Tile[,] _tiles;

    public WorldMap(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new Tile[width, height];

        for (var x = 0; x < width; x++)
            for (var y = 0; y < height; y++)
                _tiles[x, y] = new Tile { X = x, Y = y };
    }

    public Tile GetTile(int x, int y) => _tiles[x, y];

    public IEnumerable<Tile> AllTiles()
    {
        for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
                yield return _tiles[x, y];
    }

    // six hex neighbors using odd-r offset: pointy-top hexes, odd rows shift right by 0.5 tile
    public IEnumerable<Tile> GetNeighbors(int x, int y)
    {
        (int dx, int dy)[] dirs = y % 2 == 0
            ? [(-1, -1), (0, -1), (-1, 0), (1, 0), (-1, 1), (0, 1)]
            : [(0,  -1), (1, -1), (-1, 0), (1, 0), (0,  1), (1, 1)];

        foreach (var (dx, dy) in dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && nx < Width && ny >= 0 && ny < Height)
                yield return _tiles[nx, ny];
        }
    }

    public IEnumerable<Tile> GetNeighbors(Tile tile) =>
        GetNeighbors(tile.X, tile.Y);

    public IEnumerable<Population> AllPopulations() =>
        AllTiles().SelectMany(t => t.Populations);
}
