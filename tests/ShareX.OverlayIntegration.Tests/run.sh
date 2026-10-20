#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
source scripts/linux/env.sh
mode=${1:-native}
if [[ "$mode" != --headless && ( "${WAYLAND_DISPLAY:-}" != sx-* || "${WAYLAND_DISPLAY:-}" == */* ) ]]; then
    echo "Refusing: WAYLAND_DISPLAY must start with sx- (nested compositor only)." >&2
    exit 9
fi
export SX_SANDBOX=${SX_SANDBOX:-$(mktemp -d /tmp/sx-overlay-test.XXXXXX)}
mkdir -p "$SX_SANDBOX/runtime"
chmod 700 "$SX_SANDBOX/runtime"
if [[ "$mode" == --headless ]]; then
    unset DISPLAY WAYLAND_DISPLAY HYPRLAND_INSTANCE_SIGNATURE SWAYSOCK NIRI_SOCKET DBUS_SESSION_BUS_ADDRESS
    export XDG_RUNTIME_DIR="$SX_SANDBOX/runtime"
else
    # Require the caller to supply a private runtime dir and the nested compositor IPC environment.
    [[ -n "${XDG_RUNTIME_DIR:-}" && -S "$XDG_RUNTIME_DIR/$WAYLAND_DISPLAY" ]] || { echo 'Nested socket missing' >&2; exit 9; }
    unset DISPLAY HYPRLAND_INSTANCE_SIGNATURE NIRI_SOCKET SWAYSOCK
fi
dotnet build tests/ShareX.OverlayIntegration.Tests -c Release -nologo -v q -p:NuGetAudit=false --disable-build-servers -m:1 -p:UseSharedCompilation=false
if [[ "$mode" == --headless ]]; then
    dotnet tests/ShareX.OverlayIntegration.Tests/bin/Release/net10.0/ShareX.OverlayIntegration.Tests.dll --headless
else
    dotnet tests/ShareX.OverlayIntegration.Tests/bin/Release/net10.0/ShareX.OverlayIntegration.Tests.dll
fi
