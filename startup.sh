#!/bin/bash
#
# Bring up the EcosystemSim Godot frontend.
#
# WHY THE BUILD STEP: the simulation lives in C# (the EcosystemSim / EcosystemGame .NET assemblies),
# NOT in Godot assets. Godot does not reliably recompile C# when you just launch it, so running the
# editor directly can execute STALE game logic — you change the sim, relaunch, and see no difference
# (re-importing assets does nothing, because it's code, not assets). We therefore rebuild the .NET
# solution before launching. `dotnet build` on the game project writes to godot/.godot/mono/temp/bin,
# which is exactly where Godot loads its assemblies from, so the editor always starts on fresh code.
set -euo pipefail

PROJECT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GODOT_BIN="Godot_v4.7-stable_mono_linux.x86_64"

# fall back to the known install path if the binary isn't on PATH
if ! command -v "$GODOT_BIN" >/dev/null 2>&1; then
	GODOT_BIN="/usr/local/lib/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
fi

echo "==> Rebuilding C# assemblies (prevents stale-artifact runs)…"
dotnet build "$PROJECT/godot/EcosystemGame.csproj" --nologo -v minimal

echo "==> Launching Godot…"
exec "$GODOT_BIN" --path "$PROJECT/godot"
