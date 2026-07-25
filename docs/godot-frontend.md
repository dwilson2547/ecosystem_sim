# Godot Frontend

The game UI lives in `godot/` and is a Godot 4.7 (.NET / C#) project. It references
`sim/EcosystemSim/` directly via a `<ProjectReference>` — no manual DLL copy step.

---

## WSL2 build notes

- `project.godot [dotnet] project/assembly_name` **must match** the assembly name the `.csproj`
  produces. Our `.csproj` is `EcosystemGame.csproj` → assembly name defaults to `EcosystemGame`.
  Godot writes this key automatically on first open; don't change it or add `<AssemblyName>` to
  the `.csproj` unless they agree.
- The game assembly (`EcosystemGame.dll`) and the engine library (`EcosystemSim.dll`) must have
  **different names** — they land in the same build output directory.
- If you get "script is not compiling" after a crash or assembly name change, rebuild headlessly:
  ```bash
  ~/tools/godot/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 \
      --path /path/to/ecosystem_sim/godot --headless --build-solutions --quit-after 5
  ```
- **Godot 4.7 embedded Game panel does not forward input on WSL2** — even with the "Game Input"
  toggle enabled, all keyboard and mouse events are silently dropped. Always run the game from
  the terminal (see "Opening the project" below) rather than the editor's Play button.
  See [`docs/issues/2026_06_30_godot_wsl2_embedded_panel_input.md`](issues/2026_06_30_godot_wsl2_embedded_panel_input.md)
  for full details.

---

## Opening the project

```bash
~/tools/godot/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 \
    --path /path/to/ecosystem_sim/godot
```

Or open the binary, choose **Import**, and point it at `godot/project.godot`. Godot will
compile the C# project automatically on first open (requires .NET 8 SDK).

---

## Project layout

```
godot/
├── project.godot          — Godot project config (autoloads, display, renderer)
├── EcosystemGame.csproj   — C# project (Godot.NET.Sdk, refs EcosystemSim)
├── scenes/
│   └── Main.tscn          — root scene; just a Node2D with SimMain.cs attached
├── assets/
│   └── sprites/
│       ├── alamosaurus.png — processed map icon (transparent bg, two-tone brown fill)
│       └── triceratops.png — processed map icon (transparent bg, multicolor head, dull brown/grey body)
└── scripts/
    ├── SimManager.cs      — autoload singleton: owns World, drives tick timer
    ├── DemoWorldSeeder.cs — creates the demo world (mirrors SimConsole/WorldSeeder)
    ├── SimMain.cs         — root node: creates camera, renderer, HUD, panels, and mode overlay
    ├── HexMapRenderer.cs  — instantiates one HexTile per sim tile; PixelToTile + SelectTile
    ├── HexTile.cs         — one hex cell (Polygon2D terrain + Label for pop + selection border)
    ├── CameraController.cs— Camera2D with middle-mouse pan, scroll-wheel zoom
    ├── HUD.cs             — top-left panel: day / season / year / speed
    ├── FactionPanel.cs    — left-side panel: faction list, population summaries, diplomatic relations
    ├── ScenarioPanel.cs   — top-right challenge time, AP, objectives, and result
    ├── ScenarioSelectionOverlay.cs — startup Sandbox/Locust Plague/Drought Recovery picker
    ├── HistoryPanel.cs    — bottom analysis panel: population history and recent events
    ├── LineageChart.cs    — custom lineage-history graph renderer
    └── TileInfoPanel.cs   — right-side tile details and scenario interventions
```

---

## Coordinate system

The sim and renderer use **odd-r offset** hex coordinates (pointy-top hexes, odd rows shift
right by half a tile). `HexMapRenderer.HexToPixel` converts grid (col, row) → pixel position:

```
px = HexSize × √3 × (col + (row % 2 == 1 ? 0.5 : 0))
py = HexSize × 1.5 × row
```

`HexSize = 60` by default → tiles are ~104 px wide, ~90 px tall between centers.
The full 10×10 map spans roughly 1040 × 900 world pixels.

This matches `WorldMap.GetNeighbors`'s odd-r neighbor offsets exactly, so a hex tile's
rendered neighbors are its simulation neighbors.

---

## Node graph

```
[autoload] SimManager        — created before Main scene; owns World
Main (Node2D / SimMain)
├── CameraController (Camera2D)
├── HexMapRenderer (Node2D)
│   └── HexTile × 100       — one per sim tile, created in _Ready
├── HUD (CanvasLayer)
│   └── PanelContainer → VBoxContainer → Labels
├── FactionPanel (CanvasLayer)
│   └── PanelContainer (anchored left, 260px)
│       └── ScrollContainer → VBoxContainer → dynamic content
├── ScenarioPanel (CanvasLayer)
│   └── PanelContainer → objective/status content + restart/new-scenario buttons
├── HistoryPanel (CanvasLayer)
│   └── PanelContainer → History/Events views + lineage chart or event rows
├── TileInfoPanel (CanvasLayer)
│   └── PanelContainer (anchored right, 300px)
│       └── ScrollContainer → VBoxContainer → dynamic content
└── ScenarioSelectionOverlay (CanvasLayer)
    └── full-screen ColorRect → centered mode choices
```

---

## SimManager

Singleton accessed anywhere via `SimManager.Instance`.

| Property / Method | Description |
|-------------------|-------------|
| `World`           | The live `EcosystemSim.World` |
| `CurrentScenarioKind` | Selected Sandbox, Locust Plague, or Drought Recovery mode |
| `TickInterval`    | Seconds between ticks (default 2.0) |
| `Paused`          | Read/write; emits `PausedChanged` signal |
| `TogglePause()`   | Flip pause state |
| `StartScenario(kind)` | Rebuild the demo world and start the selected mode |
| `Reset()`         | Restart the current mode from a fresh seeded world |
| `RequestScenarioSelection()` | Pause and reopen the mode picker |
| `TryApplyScenarioAction(action)` | Apply an engine-validated tile intervention |
| `ToggleAnalysis()` | Open or close the History/Events panel |
| `SpeedUp()`       | Reduce interval by 0.25s (min 0.25s) |
| `SpeedDown()`     | Increase interval by 0.5s (max 8.0s) |
| signal `Ticked`   | Fired after every `World.Tick()` call |
| signal `PausedChanged(bool)` | Fired when pause state changes |

---

## Controls

| Input | Action |
|-------|--------|
| `Space`       | Pause / unpause |
| `+` / `=`     | Speed up (shorter tick interval) |
| `-`           | Slow down |
| Left click    | Select tile / deselect (click off-map) |
| Middle mouse drag | Pan camera |
| Scroll wheel  | Zoom in / out |
| `R`           | Restart the current scenario |
| `H`           | Toggle population history and event analysis |

---

## HexTile display

Each tile shows:
- **Background color** — terrain type (see `HexTile.TerrainColor`)
- **Green tint** — fertilizer > 40 units on the tile
- **Warning marker/tint** — orange below 60% population satisfaction; red below 30% or when diseased
- **Dominant population indicator** — whichever living population has the highest count on the
  tile:
  - **Species with icon art** (`HexTile.IconPaths`: currently Alamosaurus, Triceratops) — rendered
    as a cluster of up to `MaxSpeciesIcons` (5) copies of the species' icon, one icon per
    `CountPerIcon` (20) individuals, capped at 5 — quantity is read from icon count, not text.
    Looked up via `Species.EffectiveRootName` so evolved "Greater/Giant X" variants still get the
    icon. Layout is a fixed 1/2/3/4/5-icon pattern (`HexTile.IconLayouts`); tune `IconSize` /
    `IconSpacing` there. A single pool of 5 `Sprite2D` nodes is reused and re-textured for
    whichever species is dominant (only one is ever dominant on a tile at a time).
  - **Every other species** — first letter of dominant species + count (e.g. `T\n87`), letter
    changes to `G`/`L` when speciation produces "Greater"/"Lesser" variants

Adding icon art for another species: process the source art (transparent background, filled
silhouette, ~64×64) into `assets/sprites/<name>.png`, then add one entry to `HexTile.IconPaths`
keyed by that species' `RootName`/`Name` — no other code changes needed.

---

## TileInfoPanel

Left-click any hex tile to open the right-side info panel (300 px wide, scrollable). Click off-map or on empty space to deselect. Selected tile gets a bright white thicker border. Panel rebuilds live on every tick while selected.

Sections shown (each separated by a divider):
- **Header** — `(col, row)  TerrainType`
- **Resources** — per resource: `Type  amount/capacity (%)  +regen/tick`
- **Fertilizer** — shown if > 1 unit: `Fertilizer  amount`
- **Succession** — active degradation or recovery pressure and its configured trigger duration
- **Interventions** — scenario-specific actions. Locust Plague offers culling, grass restoration,
  and Meganeura seeding; Drought Recovery offers watering holes, vegetation restoration, and rain
  seeding. Sandbox offers both sets for free. Invalid actions are disabled with an inline reason,
  and completed actions show inline success/error feedback.
- **Populations** — one card per living population, sorted by count descending:
  - Species name + count, faction name, satisfaction, breeding schedule, active food/water
    deprivation pressure, size index, immunity delta, and infection details
- **Extinct** — compact grey list of zero-count historical populations on that tile

## FactionPanel

Always-visible 260 px panel on the left side. Rebuilds live on every tick.

Sections:
- **FACTIONS** — for each living faction: name + total population, then one entry per population
  group showing `(col,row)  species  ×count  sat%`; satisfaction color-coded green/yellow/orange/red;
  disease shown in red with `[SICK]` label
- **RELATIONS** — for each living-faction pair with a relation: faction names and diplomatic state
  (ALLIED=green, NEUTRAL=grey, TENSE=yellow, AT WAR=red); `[TRADE]` appended when active
- **EXTINCT** — compact grey list of fully-extinct factions (all populations Count=0)

## Scenario flow

The simulation starts paused behind `ScenarioSelectionOverlay`. Sandbox has unlimited time and
free interventions. Locust Plague runs for three in-game years with 10 shared action points;
Drought Recovery runs for two years with the same budget.

`ScenarioPanel` displays days remaining, the current AP budget, live objective values, and the
final victory/defeat message. When a challenge resolves, `SimManager` pauses automatically and
locks intervention controls. **Restart** recreates the current mode; **New scenario** pauses and
returns to the startup picker.

The Locust Plague objectives are to keep Triceratops, Alamosaurus, Parasaurolophus, and
Tyrannosaurus alive; finish with at most 500 locusts; and retain at least 25% average Plains grass.

Drought Recovery keeps the same dinosaur-survival requirement and asks the player to finish with
at least 55% average land freshwater and 50% average land vegetation. The world remains in drought
except during player-seeded 45-day rain spells.

## History and event feedback

Press `H` or use **History & events** in the HUD to open the bottom analysis panel. **History**
discovers lineages from the live world, shows a selectable 30-day population graph, and summarizes
start/current/min/max/change. Before two samples exist, it explains the sampling interval.

**Events** shows the latest 12 major engine events with year/day, severity color, and tile coordinates
for interventions and terrain transitions. The HUD mirrors the newest event in compact form. Challenge objective rows compare
the latest two history samples and show `improving`, `steady`, or `worsening`.

## What's not yet in the UI

- Disease trigger hotkey (SimConsole uses `D`)
- Trade toggle
- Procedural map generation

---

## Extending

**New player action** — implement `IWorldCommand` in `EcosystemSim`, call
`SimManager.Instance.World.Apply(cmd)` from the Godot layer (input handler or button).

**Per-tile events** — `SimManager` fires `Ticked` after every tick; subscribe in any script
to react to state changes. No polling needed.

**New HUD element** — add a `Label` child in `HUD._Ready()` and update it in `Refresh()`.

**Larger map** — change `WorldMap` size in `DemoWorldSeeder.Create()`; `HexMapRenderer`
iterates `map.Width × map.Height` automatically.
