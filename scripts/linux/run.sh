#!/usr/bin/env bash
# Run the built app with an ISOLATED home so tests never touch the real user config.
# Usage: SX_SANDBOX=/tmp/sx-mytest scripts/linux/run.sh [sharex args]
# The Wayland/X11 session variables are inherited, so windows appear on the live desktop:
# always wrap in `timeout`, and never leave fullscreen overlays running.
set -e
root="$(cd "$(dirname "$0")/../.." && pwd)"
source "$root/scripts/linux/env.sh"
sb="${SX_SANDBOX:-/tmp/sx-sandbox}"
mkdir -p "$sb"/{home,config,data,cache,state}
export HOME="$sb/home" XDG_CONFIG_HOME="$sb/config" XDG_DATA_HOME="$sb/data" XDG_CACHE_HOME="$sb/cache" XDG_STATE_HOME="$sb/state"
exec "$root/ShareX/bin/Debug/net10.0/ShareX" "$@"
