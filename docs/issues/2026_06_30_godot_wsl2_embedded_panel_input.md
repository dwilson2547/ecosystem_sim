# Godot 4.7 embedded Game panel silently drops all input on WSL2

**Date:** 2026-06-30  
**Component:** Godot editor embedded Game panel — WSL2 X11 input pipeline  
**Severity:** High — blocks all game interaction (keyboard, mouse, camera) when using the standard editor workflow on WSL2

---

## Observed symptom

After launching the game via the Godot 4.7 editor's Play button (embedded Game panel), all input was
silently ignored regardless of the "Game Input" toggle state:

- Space (pause/unpause) — no response
- `+` / `-` (speed control) — no response
- Left-click on hex tiles (tile selection) — no response, no tile highlight
- Middle-mouse drag (camera pan) — no response
- Scroll wheel (camera zoom) — no response

The simulation was running correctly (tick counter incrementing, hex map rendering, HUD updating).
The issue was exclusively in the input pipeline, not game logic or rendering.

---

## Root cause

### Godot 4.7 embedded Game panel input isolation on WSL2

Godot 4.7 introduced an embedded Game panel that runs the game inside the editor window rather than
spawning a separate OS window. This panel has a "Game Input" toggle that is supposed to forward
keyboard and mouse events to the running game.

On WSL2, the X11 forwarding layer (XWayland or VcXsrv-style) does not support the input capture
mechanism the embedded panel uses. Even with "Game Input" toggled on, events are not delivered to
the Godot viewport's input queue — they are absorbed by the editor's own window system or the X11
bridge before reaching game nodes.

This is specific to the WSL2 environment. On native Linux or Windows the embedded panel works
correctly.

### Shutdown noise from force-killed editor process

When the editor process (PID 1405831) was killed via SIGTERM, the Godot editor emitted a large
block of `ERROR: BUG: Unreferenced static string to 0:` messages covering editor UI symbols
(`EditorIcons`, `AssetLib`, `GuiChecked`, etc.) and engine internals (`_ready`, `_input`,
`_unhandled_input`, etc.), followed by `Pages in use exist at exit in PagedAllocator` errors.

These are normal Godot shutdown diagnostics that fire when the process is terminated before its
destructor sequence completes. They are not related to game code or the input bug.

---

## Troubleshooting steps taken

1. **Checked "Game Input" toggle in editor** — toggle was ON, but all input remained unresponsive;
   ruled out user configuration error.

2. **Reviewed SimMain._UnhandledInput** — confirmed keyboard branch (Space/+/-) and mouse branch
   (left-click) are structurally independent; a throw in the click branch could disable the
   callback via Godot's exception-suppression mechanism, explaining why Space stopped working after
   the first click.

3. **Added try-catch to click handler in SimMain** — isolated the click path so any runtime
   exception would print to output and not kill the callback; wrapped with `GD.PrintErr`.

4. **Reviewed CanvasLayer Control MouseFilter settings** — HUD and TileInfoPanel CanvasLayers had
   Controls with default `MouseFilter = Stop`, which can absorb mouse events before they reach
   `_UnhandledInput` on game nodes; set `MouseFilter = Ignore` on all read-only Controls.

5. **Inspected kill_log.txt (SIGTERM stack trace)** — all symbols were Godot editor UI strings,
   confirming the killed process was the editor (not just the game subprocess), and therefore the
   game had been running inside the embedded panel the whole time.

6. **Ran game from terminal with `--path` flag** — spawned a native X11 window outside the editor;
   all input (keyboard, mouse, camera) worked immediately and reliably.

---

## Fix

### Runtime workaround — run game from terminal, not editor Play button

Launch the game as a standalone process to get a native X11 window:

```bash
~/tools/godot/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64 \
    --path /home/daniel/documents/workspace/games/ecosystem_sim/godot
```

This bypasses the embedded Game panel entirely. The game window receives input normally through
the standard X11 event queue.

### `godot/scripts/SimMain.cs` — defensive try-catch in click handler

Added exception guard so a runtime throw in the click path cannot disable `_UnhandledInput` for
the entire node (which would take out keyboard handling too):

```csharp
// before
if (tile is not null) _panel.ShowTile(tile);
else _panel.HidePanel();

// after
try
{
    var tile = _hexMap.PixelToTile(worldPos);
    _hexMap.SelectTile(tile);
    if (tile is not null) _panel.ShowTile(tile);
    else _panel.HidePanel();
}
catch (Exception ex)
{
    GD.PrintErr($"[SimMain] click handler: {ex.Message}");
}
```

### `godot/scripts/HUD.cs` and `godot/scripts/TileInfoPanel.cs` — MouseFilter = Ignore on all Controls

Read-only overlay Controls had `MouseFilter = Stop` (Godot default), which absorbs mouse events
before they reach `_UnhandledInput` on world nodes. Set `Ignore` on every Control in both
CanvasLayers since neither panel has interactive elements:

```csharp
panel.MouseFilter   = Control.MouseFilterEnum.Ignore;
vbox.MouseFilter    = Control.MouseFilterEnum.Ignore;
// ... and on every Label, HSeparator, inner PanelContainer created dynamically
```

---

## Files changed

- `godot/scripts/SimMain.cs` — `_UnhandledInput` (try-catch wrapper on click handler)
- `godot/scripts/HUD.cs` — `_Ready` (MouseFilter = Ignore on panel, vbox, all labels)
- `godot/scripts/TileInfoPanel.cs` — `_Ready`, `Sep`, `AddTo`, `PopBlock` (MouseFilter = Ignore on all Controls)
