using Godot;
using EcosystemSim;

namespace EcosystemGame;

public partial class LineageChart : Control
{
    public string Lineage { get; set; } = string.Empty;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(0f, 110f);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        DrawRect(rect, new Color(0.06f, 0.08f, 0.12f, 0.95f));

        var samples = SimManager.Instance.World.State.History
            .Where(s => s.LineagePopulations.ContainsKey(Lineage))
            .ToList();
        if (samples.Count < 2) return;

        var values = samples.Select(s => s.LineagePopulations[Lineage]).ToList();
        var max = Math.Max(1, values.Max());
        var padding = 8f;
        var width = Math.Max(1f, Size.X - padding * 2f);
        var height = Math.Max(1f, Size.Y - padding * 2f);
        var points = new Vector2[samples.Count];
        for (var i = 0; i < samples.Count; i++)
        {
            var x = padding + width * i / Math.Max(1, samples.Count - 1);
            var y = padding + height * (1f - values[i] / (float)max);
            points[i] = new Vector2(x, y);
        }

        for (var i = 1; i < 4; i++)
        {
            var y = padding + height * i / 4f;
            DrawLine(new Vector2(padding, y), new Vector2(Size.X - padding, y),
                new Color(1f, 1f, 1f, 0.08f), 1f);
        }

        DrawPolyline(points, new Color(0.35f, 0.85f, 1f), 2.5f, true);
    }
}
