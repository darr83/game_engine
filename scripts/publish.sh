#!/usr/bin/env bash
# Builds a ready-to-run copy of Game_Engine for one platform into dist/<platform>.
# Usage: scripts/publish.sh win-x64   (or osx-arm64, osx-x64, linux-x64)
set -euo pipefail
RID="${1:-win-x64}"
OUT="dist/$RID"
rm -rf "$OUT"
dotnet publish src/GameEngine.Editor -c Release -r "$RID" --self-contained -p:PublishSingleFile=false -o "$OUT"
# The player is what exported games run on; the editor copies it from this folder.
dotnet publish src/GameEngine.Player -c Release -r "$RID" --self-contained -p:PublishSingleFile=false -o "$OUT/player"
echo "Done: $OUT"
