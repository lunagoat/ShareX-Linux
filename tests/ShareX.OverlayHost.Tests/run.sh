#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/linux/env.sh
unset DISPLAY WAYLAND_DISPLAY
export XDG_SESSION_TYPE=headless
dotnet build tests/ShareX.OverlayHost.Tests/ShareX.OverlayHost.Tests.csproj -c Release -nologo -v q -p:NuGetAudit=false
dotnet tests/ShareX.OverlayHost.Tests/bin/Release/net10.0/ShareX.OverlayHost.Tests.dll "$@"
