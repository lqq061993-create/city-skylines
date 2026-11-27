#!/usr/bin/env bash
# Build IndustryMaster.dll against a local Cities: Skylines II install.
#
# The game ships its own Mono profile (mscorlib.dll + netstandard.dll) in
# Cities2_Data/Managed, so the mod is compiled with the SDK's Roslyn csc against
# those assemblies instead of a .NET targeting pack.
#
# Usage: ./build/build.sh ["D:/steam/steamapps/common/Cities Skylines II"]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
game="${1:-D:/steam/steamapps/common/Cities Skylines II}"
managed="$game/Cities2_Data/Managed"
[ -d "$managed" ] || { echo "Managed folder not found: $managed" >&2; exit 1; }

dotnet_root="$(dirname "$(command -v dotnet)")"
csc="$(ls "$dotnet_root"/sdk/*/Roslyn/bincore/csc.dll 2>/dev/null | tail -1)"
[ -n "$csc" ] || { echo "csc.dll not found next to the dotnet host" >&2; exit 1; }

# Native Windows path for a file, so the response file needs no cygpath.
wfile() { printf '%s/%s' "$(cd "$(dirname "$1")" && pwd -W)" "$(basename "$1")"; }

out="$root/dist/IndustryMaster"
mkdir -p "$out" "$root/build/obj"
rsp="$root/build/obj/IndustryMaster.rsp"

{
  printf '%s\n' -nologo -nostdlib+ -target:library -langversion:latest -unsafe+ -optimize+ -deterministic+ -debug:portable
  printf -- '-out:"%s"\n' "$(wfile "$out/IndustryMaster.dll")"
  for f in "$managed"/*.dll; do printf -- '-r:"%s"\n' "$(wfile "$f")"; done
  printf -- '-r:"%s"\n' "$(wfile "$root/lib/0Harmony.dll")"
  for f in "$root"/source/*.cs; do printf '"%s"\n' "$(wfile "$f")"; done
} > "$rsp"

dotnet exec "$csc" "@$(wfile "$rsp")"

cp "$root/mod.json" "$root/IndustryMaster.mjs" "$root/lib/0Harmony.dll" "$root/lib/Harmony-LICENSE.txt" "$out/"
echo "Built: $out/IndustryMaster.dll"
