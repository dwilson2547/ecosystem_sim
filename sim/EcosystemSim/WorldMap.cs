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

    // cardinal neighbors only — keeps proximity logic simple and deterministic
    public IEnumerable<Tile> GetNeighbors(int x, int y)
    {
        if (x > 0)          yield return _tiles[x - 1, y];
        if (x < Width - 1)  yield return _tiles[x + 1, y];
        if (y > 0)          yield return _tiles[x, y - 1];
        if (y < Height - 1) yield return _tiles[x, y + 1];
    }

    public IEnumerable<Tile> GetNeighbors(Tile tile) =>
        GetNeighbors(tile.X, tile.Y);

    public IEnumerable<Population> AllPopulations() =>
        AllTiles().SelectMany(t => t.Populations);
}
