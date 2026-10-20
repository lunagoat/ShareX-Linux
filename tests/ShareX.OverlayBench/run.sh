#!/usr/bin/env bash
# CPU-only, synthetic headless rendering. No desktop session is used.
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/linux/env.sh
export NUGET_HTTP_CACHE_PATH=/tmp/sharex-overlay-nuget-cache
unset DISPLAY WAYLAND_DISPLAY
export XDG_SESSION_TYPE=headless
dotnet build tests/ShareX.OverlayBench/ShareX.OverlayBench.csproj -c Release -nologo -v q -p:NuGetAudit=false
dotnet tests/ShareX.OverlayBench/bin/Release/net10.0/ShareX.OverlayBench.dll "$@"
