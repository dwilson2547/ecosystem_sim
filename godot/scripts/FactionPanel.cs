using Godot;
using EcosystemSim;
using System.Collections.Generic;
using System.Linq;

namespace EcosystemGame;

public partial class FactionPanel : CanvasLayer
{
    private VBoxContainer _content = null!;

    public override void _Ready()
    {
        var panel = new PanelContainer();
        panel.AnchorLeft   = 0f;
        panel.AnchorTop    = 0f;
        panel.AnchorRight  = 0f;
        panel.AnchorBottom = 1f;
        panel.OffsetLeft   = 0f;
        panel.OffsetRight  = 260f;
        panel.OffsetTop    = 0f;
        panel.OffsetBottom = 0f;
        panel.MouseFilter  = Control.MouseFilterEnum.Ignore;
        AddChild(panel);

        var scroll = new ScrollContainer();
        scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        scroll.MouseFilter       = Control.MouseFilterEnum.Ignore;
        panel.AddChild(scroll);

        _content = new VBoxContainer();
        _content.CustomMinimumSize = new Vector2(240f, 0f);
        _content.MouseFilter       = Control.MouseFilterEnum.Ignore;
        scroll.AddChild(_content);

        SimManager.Instance.Ticked += Rebuild;
        SimManager.Instance.WorldReset += Rebuild;
        Rebuild();
    }

    private void Rebuild()
    {
        while (_content.GetChildCount() > 0)
        {
            var child = _content.GetChild(0);
            _content.RemoveChild(child);
            child.Free();
        }

        var factions = SimManager.Instance.World.State.Factions;
        var map      = SimManager.Instance.World.State.Map;

        Row("FACTIONS", size: 13, color: new Color(0.7f, 0.9f, 1f));
        Sep();

        var living = factions.Where(f => !f.IsExtinct).ToList();
        var dead   = factions.Where(f => f.IsExtinct).ToList();

        foreach (var faction in living)
            FactionBlock(faction, map);

        // Relations between living factions
        var seen          = new HashSet<(Faction, Faction)>();
        var anyRelations  = false;
        foreach (var faction in living)
        {
            foreach (var (other, rel) in faction.Relations)
            {
                if (!living.Contains(other)) continue;
                if (seen.Contains((other, faction))) continue;
                seen.Add((faction, other));
                if (!anyRelations)
                {
                    Row("RELATIONS", size: 13, color: new Color(0.7f, 0.9f, 1f));
                    Sep();
                    anyRelations = true;
                }
                RelationRow(faction, other, rel);
            }
        }

        if (dead.Count > 0)
        {
            if (!anyRelations) Sep();
            Row("EXTINCT", size: 11, color: new Color(0.4f, 0.4f, 0.4f));
            foreach (var f in dead)
                Row($"  {f.Name}", color: new Color(0.35f, 0.35f, 0.35f));
        }
    }

    private void FactionBlock(Faction faction, WorldMap map)
    {
        Row($"{faction.Name}  ×{faction.TotalPopulation}", size: 13, color: Colors.White);

        var pops = faction.Populations
            .Where(p => p.Count > 0)
            .OrderByDescending(p => p.Count);

        foreach (var pop in pops)
        {
            var tile = pop.CurrentTile ?? map.AllTiles().First(t => t.Populations.Contains(pop));
            var sat  = (int)(pop.LastSatisfaction * 100);
            var name = pop.Species.Name;
            if (name.Length > 18) name = name[..18];

            var suffix = pop.Disease is not null ? " [SICK]" : string.Empty;
            var lineColor = pop.Disease is not null
                ? new Color(1f, 0.4f, 0.4f)
                : SatColor(pop.LastSatisfaction);

            Row($"  ({tile.X},{tile.Y}) {name}", color: new Color(0.8f, 0.8f, 1f));
            Row($"        ×{pop.Count}  {sat}%{suffix}", color: lineColor);
        }
        Sep();
    }

    private void RelationRow(Faction a, Faction b, FactionRelation rel)
    {
        var (label, color) = rel.State switch
        {
            DiplomaticState.Allied  => ("ALLIED",  new Color(0.4f, 1.0f, 0.4f)),
            DiplomaticState.Neutral => ("NEUTRAL", new Color(0.6f, 0.6f, 0.6f)),
            DiplomaticState.Tense   => ("TENSE",   new Color(1.0f, 1.0f, 0.4f)),
            DiplomaticState.AtWar   => ("AT WAR",  new Color(1.0f, 0.3f, 0.3f)),
            _                       => ("?",        Colors.Gray),
        };
        var trade = rel.HasTradeAgreement ? " [TRADE]" : string.Empty;
        Row($"  {a.Name}", color: new Color(0.75f, 0.75f, 0.75f));
        Row($"  ←→ {b.Name}", color: new Color(0.75f, 0.75f, 0.75f));
        Row($"  {label}{trade}", color: color);
        Sep();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void Row(string text, int size = 12, Color? color = null)
    {
        var lbl = new Label { Text = text, MouseFilter = Control.MouseFilterEnum.Ignore };
        lbl.AddThemeFontSizeOverride("font_size", size);
        if (color.HasValue)
            lbl.AddThemeColorOverride("font_color", color.Value);
        _content.AddChild(lbl);
    }

    private void Sep()
    {
        var sep = new HSeparator();
        sep.MouseFilter = Control.MouseFilterEnum.Ignore;
        _content.AddChild(sep);
    }

    private static Color SatColor(float sat) => sat switch
    {
        >= 0.9f => new Color(0.4f, 1.0f, 0.4f),
        >= 0.6f => new Color(1.0f, 1.0f, 0.4f),
        >= 0.3f => new Color(1.0f, 0.55f, 0.2f),
        _       => new Color(1.0f, 0.3f, 0.3f),
    };
}
