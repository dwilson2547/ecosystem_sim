<#
.SYNOPSIS
  Drives the EcosystemSim Godot game for an agent: launch, screenshot, click, key, stop.

.DESCRIPTION
  Godot is a native win32 app (not something chromium-cli/Playwright can attach to), so this
  driver uses Win32 GDI/user32 calls directly: screen capture for screenshots, synthetic
  mouse/key input for interaction.

  IMPORTANT: every action here is a SEPARATE, SELF-CONTAINED `powershell -File` invocation.
  Add-Type state does not persist between calls, and -- more importantly -- if this driver is
  being run by an agent from inside an editor/terminal host (e.g. VS Code), THAT HOST WINDOW
  STEALS FOREGROUND FOCUS between tool calls. Every action that touches the screen (screenshot,
  click, key) therefore re-asserts foreground focus on the Godot window itself, immediately
  before acting, every single time. Do not "optimize" this away by focusing once and reusing it
  across calls -- it will silently capture/click the wrong window. See SKILL.md Gotchas.

.EXAMPLE
  powershell -File driver.ps1 -Action launch          # invoke with run_in_background:true !
  powershell -File driver.ps1 -Action wait             # separate call, confirms window is up
  powershell -File driver.ps1 -Action status
  powershell -File driver.ps1 -Action screenshot -Out C:\tmp\shot.png
  powershell -File driver.ps1 -Action click -X 1264 -Y 656
  powershell -File driver.ps1 -Action key -Keys "{ESC}"
  powershell -File driver.ps1 -Action stop
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("find-exe", "launch", "wait", "screenshot", "click", "key", "stop", "status")]
    [string]$Action,

    [string]$TitleLike = "EcosystemSim",
    [int]$X,
    [int]$Y,
    [string]$Out = "$env:TEMP\godot_shot.png",
    [string]$Keys
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class GodotWin32 {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int X, int Y);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
}
"@

# CRITICAL: without this, PowerShell runs DPI-unaware, so Windows silently rescales every
# coordinate GetWindowRect/SetCursorPos/CopyFromScreen see. Godot itself IS DPI-aware, so its
# real window rect is in true physical pixels -- an unaware caller's "same" coordinates land
# somewhere else on screen entirely (clicks land on the wrong UI row, screenshots subtly
# mis-measure). This one call makes screenshot pixels and click coordinates agree. See
# SKILL.md Gotchas -- this was the cause of clicks reliably landing one button-row too low.
[GodotWin32]::SetProcessDPIAware() | Out-Null

function Find-GodotExe {
    # No project-local install; search the usual per-user drop locations for the 4.7 mono build.
    # Must be the _console variant -- see SKILL.md Gotchas for why the non-console .exe opens
    # the editor instead of running the game.
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Godot*\Godot*win64_console.exe",
        "$env:USERPROFILE\.local\Godot*\**\Godot*win64_console.exe",
        "$env:USERPROFILE\Downloads\Godot*\**\Godot*win64_console.exe",
        "C:\Program Files\Godot*\Godot*win64_console.exe"
    )
    foreach ($pattern in $candidates) {
        $hit = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue -Recurse -Depth 3 |
            Select-Object -First 1
        if ($hit) { return $hit.FullName }
    }
    throw "Godot console executable not found. Install Godot 4.7 (mono) and adjust the search patterns in Find-GodotExe."
}

function Get-GodotWindow([string]$titleLike) {
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.ProcessName -like "*Godot*" -and $_.MainWindowTitle -like "*$titleLike*" } |
        Select-Object -First 1
}

switch ($Action) {

    "find-exe" {
        Find-GodotExe
    }

    "launch" {
        # MUST be invoked with the tool's run_in_background:true. This BLOCKS for the entire
        # game session (does not return until the window closes) -- that's intentional. See the
        # big Gotchas entry in SKILL.md: `Start-Process` (detach-and-return) gets its whole
        # process tree killed the instant this script exits, because the tool sandbox runs
        # commands in a job object with kill-on-close semantics. Blocking here keeps this
        # script's own process (and therefore its child, Godot) alive for as long as needed.
        $exe = Find-GodotExe
        $projectDir = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
        & $exe --path $projectDir
        "game process exited with code $LASTEXITCODE"
    }

    "wait" {
        # run after -Action launch (in a SEPARATE call) to confirm the window actually came up
        $deadline = (Get-Date).AddSeconds(20)
        $w = $null
        while ((Get-Date) -lt $deadline) {
            $w = Get-GodotWindow "DEBUG"
            if ($w) { break }
            Start-Sleep -Milliseconds 750
        }
        if (-not $w) {
            throw "Game window ('...(DEBUG)') did not appear within 20s. Check that the launch " +
                  "task is still running (it blocks for the whole session) and that it was started " +
                  "with run_in_background:true -- see SKILL.md Gotchas."
        }
        "game running: pid=$($w.Id) title='$($w.MainWindowTitle)' handle=$($w.MainWindowHandle)"
    }

    "screenshot" {
        $w = Get-GodotWindow $TitleLike
        if (-not $w) { throw "No Godot window matching title '*$TitleLike*'. Use -Action status to list open windows." }
        $h = $w.MainWindowHandle
        [GodotWin32]::ShowWindow($h, 9) | Out-Null
        [GodotWin32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 400
        [GodotWin32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 300

        $rect = New-Object GodotWin32+RECT
        [GodotWin32]::GetWindowRect($h, [ref]$rect) | Out-Null
        $width = $rect.Right - $rect.Left
        $height = $rect.Bottom - $rect.Top

        $bmp = New-Object System.Drawing.Bitmap $width, $height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($rect.Left, $rect.Top, 0, 0, (New-Object System.Drawing.Size $width, $height))
        $bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()

        "saved $Out | window rect: left=$($rect.Left) top=$($rect.Top) size=${width}x${height}"
    }

    "click" {
        if (-not $PSBoundParameters.ContainsKey('X') -or -not $PSBoundParameters.ContainsKey('Y')) {
            throw "-X and -Y are required (window-relative pixels, top-left origin -- same coordinate space as the screenshot PNG)."
        }
        $w = Get-GodotWindow $TitleLike
        if (-not $w) { throw "No Godot window matching title '*$TitleLike*'." }
        $h = $w.MainWindowHandle
        [GodotWin32]::ShowWindow($h, 9) | Out-Null
        [GodotWin32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 400
        [GodotWin32]::SetForegroundWindow($h) | Out-Null
        Start-Sleep -Milliseconds 200

        $rect = New-Object GodotWin32+RECT
        [GodotWin32]::GetWindowRect($h, [ref]$rect) | Out-Null
        $screenX = $rect.Left + $X
        $screenY = $rect.Top + $Y
        [GodotWin32]::SetCursorPos($screenX, $screenY) | Out-Null
        Start-Sleep -Milliseconds 150
        [GodotWin32]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero) # LEFTDOWN
        Start-Sleep -Milliseconds 80
        [GodotWin32]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero) # LEFTUP

        "clicked window-relative ($X,$Y) => screen ($screenX,$screenY)"
    }

    "key" {
        # UNVERIFIED / LIKELY INEFFECTIVE: SendKeys posts synthetic window messages, but Godot
        # reads keyboard state at a lower level and never saw them in testing -- sending "{ESC}"
        # or " " (space) here to a *running game* window had no observable effect (sim kept
        # ticking, HUD stayed on "Pause"). Prefer clicking the equivalent on-screen button
        # instead (-Action click) -- that route is proven reliable. Kept only in case a future
        # Godot/OS combination behaves differently; verify with a screenshot before trusting it.
        if (-not $Keys) { throw "-Keys is required, e.g. -Keys '{ESC}' or -Keys ' ' for spacebar (SendKeys syntax)." }
        $w = Get-GodotWindow $TitleLike
        if (-not $w) { throw "No Godot window matching title '*$TitleLike*'." }
        [GodotWin32]::SetForegroundWindow($w.MainWindowHandle) | Out-Null
        Start-Sleep -Milliseconds 300
        [System.Windows.Forms.SendKeys]::SendWait($Keys)
        "sent keys '$Keys' to '$($w.MainWindowTitle)' -- UNVERIFIED, confirm with a screenshot"
    }

    "status" {
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -like "*Godot*" } |
            Select-Object Id, ProcessName, MainWindowTitle, MainWindowHandle |
            Format-Table -AutoSize | Out-String
    }

    "stop" {
        Get-Process -ErrorAction SilentlyContinue |
            Where-Object { $_.ProcessName -like "*Godot*" } |
            Stop-Process -Force
        "stopped all Godot processes"
    }
}
