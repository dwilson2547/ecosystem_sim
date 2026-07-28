# Driving the Godot game from an agent on Windows

**Date:** 2026-07-27  
**Component:** `.claude/skills/run-ecosystemsim` driver — Win32 automation; also two feature
additions delivered in the same session (resolution/fullscreen settings, seamless grass texture)  
**Severity:** N/A — tooling investigation, not a bug report

---

## Summary

Added two small player-facing features to the Godot frontend, then built and verified a
`.claude/skills/run-ecosystemsim` skill so a future agent can launch, screenshot, and click
through the game on Windows without rediscovering the same automation pitfalls from scratch.

**Features added:**
- A Settings row (resolution dropdown: 720p/1080p/1440p, plus a Fullscreen toggle) on the
  scenario selection screen, persisted across launches via `GameSettings` → `user://settings.cfg`.
- A seamless, tileable grass texture for Plains hex tiles (`godot/assets/grass.png`), wired into
  `HexTile.cs` via `Polygon2D.Texture` with a per-tile UV offset so the pattern reads as one
  continuous surface across adjacent hexes instead of an obviously-repeating per-tile stamp.

**Tooling:** a PowerShell driver (`driver.ps1`) that drives the native win32 Godot window directly
via GDI/user32 calls — screen capture for screenshots, synthetic mouse input for clicks — since
Godot is not something `chromium-cli`/Playwright can attach to.

Both features were verified against the *actual running game*, not just `dotnet build`: cycled
through all three resolutions and fullscreen on/off, confirming both the visible window resize
and `settings.cfg` persistence at each step; confirmed the grass texture tiles seamlessly across
hex borders in a live Sandbox session.

---

## Gotchas discovered

Getting a reliable driver took several rounds of "this looks like Godot crashing" that turned out
to be the automation environment, not the game. In order of how confusing each was to diagnose:

### 1. The tool sandbox kills detached child processes (Job Object kill-on-close)

`Start-Process -FilePath $exe ... | Out-Null` (launch, don't wait) returned immediately, and Godot
died within ~1-4 seconds with no error text — exit codes 0, 1, or 255 depending on the run, no
stack trace, nothing actionable in the console output. Looked exactly like a random Godot crash.

**Root cause:** the shell tool runs commands inside a Windows Job Object with kill-on-close
semantics. The instant the invoking `powershell.exe` process exits, every descendant — including
a "successfully launched and detached" Godot process — gets force-terminated with it.

**Fix:** launch synchronously (`& $exe --path $dir`, which blocks until Godot closes) and mark
*that* tool call `run_in_background:true`. The wrapping PowerShell process then stays alive for
the whole game session, so the Job Object never closes out from under it.

### 2. DPI-unaware caller ⇒ clicks land on the wrong UI row

PowerShell runs DPI-unaware by default; Godot is DPI-aware. A screenshot (`CopyFromScreen`) and a
click (`SetCursorPos`) computed from measuring that screenshot ended up disagreeing about where
things were. Concretely: clicking the pixel-verified center of "Start Sandbox" reliably landed on
"Start Challenge" (Locust Plague) one row down — twice in a row, no error, just silently the wrong
button.

**Fix:** call `SetProcessDPIAware()` once at driver startup, before any `GetWindowRect` /
`SetCursorPos` / `CopyFromScreen` call. Confirmed the fix directly: the measured window rect
jumped from a DPI-virtualized `2062×1190` to the true `2578×1487`, and the same click math then
landed on the correct button every time afterward.

### 3. The host terminal steals focus between tool calls

A screenshot or click that doesn't re-assert `SetForegroundWindow` (+ a short sleep) *immediately
before* acting captures or clicks the terminal/editor hosting the agent instead of the game —
observed directly (a screenshot without a fresh focus step captured the VS Code window, not
Godot, even though a previous call had focused Godot correctly).

This also dismisses **popup controls** (`OptionButton` dropdowns, context menus): they render as a
separate OS-level window, and opening one in one tool call then clicking an item in a *separate*
call reliably closes the popup before the second click lands, because the intervening call steals
focus. The fix is to open and select in one atomic script. A popup also isn't inside the game
window's rect, so `-Action screenshot` won't show it — verify with a full virtual-screen capture
(`[System.Windows.Forms.SystemInformation]::VirtualScreen`) instead.

### 4. Keyboard synthesis doesn't reach Godot's input system

Tried both `SendKeys.SendWait(" ")` and hardware-level `keybd_event(0x20, ...)` to trigger the
documented Space = Pause/Resume shortcut (confirmed via the HUD's own tooltip text). Neither had
any effect — the sim kept ticking and the HUD label stayed on "Pause" both times, even after
explicitly focusing the window and clicking the game canvas first to guarantee input focus.

Clicking the actual on-screen "Pause" button worked immediately. **Conclusion:** don't rely on
synthetic keyboard input against this app; drive it through visible UI buttons only.

### 5. Must use the console-variant executable

`Godot*win64.exe --path <project>` opened the **editor**, not the game. The identically-invoked
`Godot*win64_console.exe --path <project>` runs the game directly — no editor/F5 hop needed. The
driver's `Find-GodotExe` specifically searches for `*win64_console.exe`.

### 6. Relaunch too soon after a force-kill and it silently fails again

Immediately re-launching right after `-Action stop` reproduced symptom #1 intermittently even
with the blocking-launch fix in place. A ~3-5 second pause between `stop` and the next `launch`
avoided it consistently in retesting.

---

## Verification

- Grass texture: launched a live Sandbox session, screenshotted the hex map, confirmed Plains
  tiles render the texture continuously across hex borders (no visible per-tile repeat).
- Resolution: reset `user://settings.cfg` to a clean baseline, then via the driver — opened the
  dropdown, selected 1920×1080 (window rect changed `1298×767` → `1938×1127`, confirmed in
  `settings.cfg`), selected 2560×1440 (rect → `2578×1487`, confirmed), checked Fullscreen (rect →
  `(0,0) 2560×1442`, no window chrome, resolution dropdown correctly auto-disabled, confirmed in
  `settings.cfg`), then unchecked it (back to windowed `1298×767`, confirmed).

---

## Files changed

- `godot/scripts/GameSettings.cs` — new; resolution/fullscreen state, `Apply()`/`Save()`/`Load()`
  against `user://settings.cfg`
- `godot/scripts/ScenarioSelectionOverlay.cs` — Settings row (resolution `OptionButton` +
  Fullscreen `CheckBox`) under the scenario list
- `godot/scripts/SimManager.cs` — `GameSettings.Load()`/`Apply()` on startup
- `godot/scripts/HexTile.cs` — `Polygon2D.Texture` + per-tile UV offset for terrain textures
  (currently just `TerrainType.Plains` → grass)
- `godot/assets/grass.png` — seamless tileable grass texture; `grass_handdrawn_original.png` keeps
  the original hand-drawn (non-seamless) first pass
- `generate_grass_texture.py` — repo-root Python/PIL script that generates the seamless tile
  (torus-wrapped strokes so every edge continues into its opposite edge); rerun to retune
  density/stroke width
- `.claude/skills/run-ecosystemsim/driver.ps1`, `SKILL.md` — the agent-drivable launch/screenshot/
  click/key/stop driver and its documentation, including all the gotchas above
