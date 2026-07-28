---
name: run-ecosystemsim
description: Build, run, and drive the EcosystemSim Godot game. Use when asked to run/launch/start the game, build the Godot project, screenshot the game UI, or click through it (scenario selection, the hex map, HUD buttons) to verify a change.
---

EcosystemSim is a Godot 4.7 (.NET/mono) desktop game — a native win32 app, not something
`chromium-cli`/Playwright can attach to. It's driven here with a PowerShell script,
`.claude/skills/run-ecosystemsim/driver.ps1`, that uses Win32 GDI/user32 calls directly:
screen capture for screenshots, synthetic mouse input for clicks. **Read the Gotchas section
below before using the driver** — two of its behaviors (how you must launch it, and a DPI fix)
are not optional and will silently produce wrong results if skipped.

All paths below are relative to `godot/` (this skill's unit).

## Prerequisites

Windows, with Godot 4.7 (mono/.NET) installed somewhere under the current user's profile.
This machine has it at:

```
C:\Users\buuua\.local\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64\Godot_v4.7-stable_mono_win64_console.exe
```

`driver.ps1 -Action find-exe` searches `%LOCALAPPDATA%\Programs`, `%USERPROFILE%\.local`,
`%USERPROFILE%\Downloads`, and `C:\Program Files` for `Godot*win64_console.exe` (recurse depth
3) and doesn't need the path hardcoded — but if Godot lives somewhere else, add a pattern to
`Find-GodotExe` in the driver.

## Build

```bash
cd godot
dotnet build EcosystemGame.csproj
```

Verified this session — `Build succeeded. 0 Warning(s). 0 Error(s).`

## Run (agent path)

Use `.claude/skills/run-ecosystemsim/driver.ps1`. Every action is a separate
`powershell -File driver.ps1 -Action <action> [...]` call.

```
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action launch     # run_in_background:true, REQUIRED (see Gotchas)
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action wait       # separate call, confirms the window is up
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action status
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action screenshot -Out C:\tmp\shot.png
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action click -X 1268 -Y 664
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action key -Keys "{ESC}"   # unverified, see Gotchas -- prefer click
powershell -File .claude/skills/run-ecosystemsim/driver.ps1 -Action stop
```

| action | what it does |
|---|---|
| `find-exe` | prints the resolved Godot console exe path |
| `launch` | blocks for the whole game session; **must** be started with the tool's `run_in_background:true` |
| `wait` | separate call, polls up to 20s for the `...(DEBUG)` window to appear |
| `screenshot -Out <path>` | focuses the window, saves a PNG of its full client+chrome rect |
| `click -X <int> -Y <int>` | window-relative pixels, top-left origin — same coordinate space as the screenshot PNG |
| `key -Keys <SendKeys string>` | unverified — see Gotchas, prefer `click` |
| `status` | lists any running Godot processes and their window titles/handles |
| `stop` | force-kills all Godot processes |

Verified end-to-end this session: launch → wait → screenshot of the "ECOSYSTEMSIM" scenario
selection screen → click "Start Sandbox" (`-X 1268 -Y 664` at 2560×1440) → screenshot showing
the hex map with the "SANDBOX / Unlimited time · Unlimited actions" panel confirming the click
landed correctly → click the "Pause" HUD button (`-X 2034 -Y 66`) → screenshot confirming the
label flipped to "Resume".

**Reading coordinates off a screenshot**: the tool may report the image to you pre-scaled
("displayed at 2000x1154, multiply by 1.03") — multiply the coordinates you read off the
*displayed* image by that factor before passing them to `-Action click`. Getting this wrong is
easy to miss because the click still "succeeds" (no error) — it just lands on the wrong UI
element. Confirm with a follow-up screenshot, not just the click command's exit code.

## Run (human path)

Open `project.godot` in the Godot editor and press F5 (or double-click the console exe with
`--path <this project dir>` and no other args) — a window opens, playable normally. Ctrl+Alt+F4
or just closing the window stops it.

## Test

```bash
cd sim
dotnet test
```

---

## Gotchas

- **Popup controls (`OptionButton` dropdowns, context menus) render as a separate OS window, and
  get dismissed by the same focus-stealing described below — you must open AND click the item in
  one atomic script, not two separate `-Action click` calls.** Confirmed while testing the
  Resolution dropdown: `-Action click` on the dropdown (separate call) opened the popup, but the
  very next separate call (even just a screenshot) stole focus and closed it before a follow-up
  click could land on an option. The popup itself isn't captured by `-Action screenshot` either
  (it's outside the game window's rect) — verify it opened by capturing the full virtual screen
  instead (`[System.Windows.Forms.SystemInformation]::VirtualScreen` + `CopyFromScreen`). Once
  open+select is combined into one script (focus window → click dropdown → sleep → click the
  option's absolute screen coordinates, all before returning), it works reliably — confirmed
  switching through all three resolutions and toggling Fullscreen this way. Plain buttons and
  checkboxes (not popups) are unaffected and work fine via separate `-Action click` calls.

- **Measure button/checkbox coordinates by cropping and zooming the actual screenshot, not by
  eyeballing the scaled preview.** Eyeballed coordinates on the Fullscreen checkbox were off by
  over 250px twice in a row (landed on empty space / the wrong control entirely, with no error —
  the click just silently did nothing or hit something unintended). Cropping a small region
  around the target with PIL and zooming it (e.g. `im.crop((x0,y0,x1,y1)).resize((...*3,...*3))`)
  before reading off pixel coordinates was reliable every time; guessing from the full-size
  preview was not.

- **`launch` MUST run synchronously (`&`), and the tool call MUST be `run_in_background:true`
  — `Start-Process` (detach-and-return) does not work.** This tool sandboxes shell commands in
  a Windows Job Object with kill-on-close semantics: every descendant process dies the instant
  the invoking `powershell.exe` exits. `Start-Process -FilePath $exe ... | Out-Null` returns
  immediately, so the wrapper process exits, so Godot gets killed within a second — it looked
  like Godot was randomly crashing on launch (exit codes 0/1/255, no error text) until this was
  traced back to the job object, not Godot. The fix: `& $exe --path $dir` *blocks* until Godot
  closes, so the wrapping process (and therefore Godot) stays alive for as long as the tool call
  is tracked — which is exactly what `run_in_background:true` does. A foreground call that
  happens to block past the 120s default timeout also gets auto-promoted to a tracked background
  task by the harness and survives the same way, but don't rely on that — request
  `run_in_background:true` explicitly.

- **Call `SetProcessDPIAware()` before any GetWindowRect/SetCursorPos/CopyFromScreen calls, or
  every click lands on the wrong UI row.** PowerShell runs DPI-unaware by default; Godot is
  DPI-aware. Windows silently rescales coordinates for an unaware caller, so a screenshot
  (captured via `CopyFromScreen`) and a click (`SetCursorPos`) computed from measuring that
  screenshot end up in *different* coordinate spaces. Symptom observed directly: clicking the
  pixel-verified center of "Start Sandbox" reliably landed on "Start Challenge" (Locust Plague)
  one button-row down, twice in a row, with no error — it just silently clicked the wrong thing.
  Adding `SetProcessDPIAware()` at driver startup fixed it immediately (confirmed: window rect
  measurement jumped from 2062×1190 to the true 2578×1487, and the same click math then landed
  correctly). The driver already does this; don't remove it.

- **The terminal/editor hosting the agent (e.g. VS Code) steals foreground focus between
  separate tool calls.** A screenshot or click that doesn't re-assert
  `SetForegroundWindow`+sleep *immediately before* acting will capture/click the host app
  instead of Godot — observed directly (a screenshot call without a fresh focus step captured
  the VS Code window instead of the game, even though the previous call had focused Godot).
  Every action in the driver that touches the screen re-focuses every time; don't "optimize"
  that away by focusing once and reusing it.

- **Keyboard input (`SendKeys` and hardware-level `keybd_event`) does not reach Godot's input
  system — use mouse clicks on the visible UI instead.** Tested both: sent spacebar (the
  documented Pause/Resume shortcut, confirmed via the HUD tooltip "Pause/resume [Space]") via
  `SendKeys.SendWait(" ")` and via `keybd_event(0x20, ...)` after explicitly focusing the window
  and even after clicking the game canvas first — the sim kept ticking and the HUD stayed on
  "Pause" both times. Clicking the actual "Pause" *button* worked immediately and reliably. Only
  routes through `-Action click` for anything the game exposes as a clickable button.

- **`--path <project>` on the non-console `.exe` opens the Godot *editor*, not the game.** The
  console-variant exe with the same `--path <project>` argument runs the game directly, no
  editor/F5 step needed. `Find-GodotExe` in the driver specifically searches for
  `*win64_console.exe`; don't switch it to the non-console build.

- **After force-killing Godot, wait a few seconds before relaunching.** Relaunching immediately
  after `-Action stop` intermittently produced the same fast-exit symptom described above (before
  the job-object cause was identified, this looked like a separate flake). A ~3-5s pause between
  `stop` and the next `launch` avoided it in every retest.

## Troubleshooting

- **`launch` exits with code 0/1/255 within ~1-4 seconds, no error text, no window ever
  appears**: this is the Job Object issue above — you (or the calling agent) invoked `launch`
  without `run_in_background:true`, or something upstream used `Start-Process` instead of the
  blocking `&`. Re-run with `run_in_background:true`.

- **`wait` throws "Game window did not appear within 20s" but `launch` is still running**:
  usually a compile error. Godot will still open *something* (often the editor, with the error in
  Output/Debug Console) if the C# build failed. Run `dotnet build EcosystemGame.csproj` first to
  rule this out before blaming the driver.

- **A click "succeeds" (driver prints the screen coordinates, no error) but nothing visibly
  changed**: almost always the DPI or focus-stealing issue above. Take a screenshot immediately
  after the click and check pixel content, don't trust the click command's exit code alone.
