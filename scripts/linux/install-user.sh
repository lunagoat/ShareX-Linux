#!/usr/bin/env bash
# Install ShareX for the current user into ~/.local. No sudo, no system files,
# no shell/desktop configuration is modified.
#
#   scripts/linux/install-user.sh [--from DIR] [--no-publish] [--uninstall] [--quiet]
#
#   --from DIR     install a previously published directory (default dist/linux-x64)
#   --no-publish   do not run scripts/linux/publish.sh when the build is missing
#   --uninstall    remove everything this script installed
#   --quiet        only print errors
#
# The script is idempotent: running it again replaces the previous installation.
# Honors $HOME, $XDG_DATA_HOME and $XDG_BIN_HOME, so it can be tested in a
# throw-away home directory.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
quiet=0
from=""
do_publish=1
uninstall=0

log() { [ "$quiet" -eq 1 ] || printf '%s\n' "$*"; }

while [ "$#" -gt 0 ]; do
    case "$1" in
        --from) from="$2"; shift 2 ;;
        --no-publish) do_publish=0; shift ;;
        --uninstall) uninstall=1; shift ;;
        --quiet) quiet=1; shift ;;
        -h|--help) sed -n '2,14p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "install-user.sh: unknown argument: $1" >&2; exit 2 ;;
    esac
done

if [ "$(id -u)" -eq 0 ]; then
    echo "install-user.sh: refusing to run as root; this is a per-user install" >&2
    exit 1
fi

home="${HOME:?}"
bindir="${XDG_BIN_HOME:-$home/.local/bin}"
libbase="$home/.local/lib"
libdir="$libbase/sharex"
datadir="${XDG_DATA_HOME:-$home/.local/share}"
appsdir="$datadir/applications"
iconsdir="$datadir/icons/hicolor"
mimedir="$datadir/mime/packages"
metainfodir="$datadir/metainfo"
desktop_id="sharex.desktop"
metainfo_id="io.github.ShareX.ShareX.metainfo.xml"

packaging_linux="$root/packaging/linux"

uninstall_all() {
    log "Removing ShareX from $home/.local"
    rm -f "$bindir/sharex"
    rm -rf "$libdir"
    rm -f "$appsdir/$desktop_id"
    rm -f "$mimedir/sharex.xml"
    rm -f "$metainfodir/$metainfo_id"
    for size in 16 24 32 48 64 128 256; do
        rm -f "$iconsdir/${size}x${size}/apps/sharex.png"
        rmdir "$iconsdir/${size}x${size}/apps" 2>/dev/null || true
    done
    rm -f "$iconsdir/scalable/apps/sharex.svg"
    rmdir "$iconsdir/scalable/apps" 2>/dev/null || true

    # Refresh the caches we may have created, ignoring missing tools.
    command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$appsdir" 2>/dev/null || true
    command -v update-mime-database >/dev/null 2>&1 && update-mime-database "$datadir/mime" 2>/dev/null || true
        log "Uninstalled. (Configuration in ShareX's personal folder was left untouched.)"
}

if [ "$uninstall" -eq 1 ]; then
    uninstall_all
    exit 0
fi

if [ -z "$from" ]; then
    from="$root/dist/linux-x64"
fi

if [ ! -x "$from/ShareX" ]; then
    if [ "$do_publish" -eq 1 ]; then
        log "No published build in $from, publishing now..."
        "$root/scripts/linux/publish.sh" >/dev/null
    else
        echo "install-user.sh: no published build found in $from" >&2
        echo "Run scripts/linux/publish.sh first, or pass --from DIR." >&2
        exit 1
    fi
fi

if [ ! -x "$from/ShareX" ]; then
    echo "install-user.sh: $from/ShareX is missing after publishing" >&2
    exit 1
fi

version="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$root/Directory.build.props" | head -n1)"
[ -n "$version" ] || version="unknown"

log "Installing ShareX $version"
log "  application : $libdir"
log "  launcher    : $bindir/sharex"
log "  desktop file: $appsdir/$desktop_id"

install -d "$libdir" "$bindir" "$appsdir" "$mimedir" "$metainfodir"

# Application files (everything the publish produced, including the native
# messaging host manifests and the file icon).
for f in "$from"/*; do
    [ -f "$f" ] || continue
    install -m644 "$f" "$libdir/$(basename "$f")"
done
chmod 755 "$libdir/ShareX"
printf '%s\n' "$version" > "$libdir/version"

install -m755 "$packaging_linux/sharex" "$bindir/sharex"
# Keep a copy of this script so the installation can be removed without the source tree.
install -m755 "${BASH_SOURCE[0]}" "$libdir/uninstall.sh"
install -m644 "$packaging_linux/$desktop_id" "$appsdir/$desktop_id"
install -m644 "$packaging_linux/sharex.xml" "$mimedir/sharex.xml"
install -m644 "$packaging_linux/$metainfo_id" "$metainfodir/$metainfo_id"

for size in 16 24 32 48 64 128 256; do
    install -Dm644 "$packaging_linux/icons/hicolor/${size}x${size}/apps/sharex.png" \
        "$iconsdir/${size}x${size}/apps/sharex.png"
done
install -Dm644 "$packaging_linux/icons/hicolor/scalable/apps/sharex.svg" \
    "$iconsdir/scalable/apps/sharex.svg"

command -v update-desktop-database >/dev/null 2>&1 && update-desktop-database -q "$appsdir" || true
command -v update-mime-database >/dev/null 2>&1 && update-mime-database "$datadir/mime" || true
# No gtk-update-icon-cache here: a user-level hicolor directory has no index.theme, so GTK would reject the cache.

log "Installed."
case ":$PATH:" in
    *":$bindir:"*) ;;
    *)
        log
        log "Note: $bindir is not in your PATH. Add it, for example:"
        log "  export PATH=\"\$HOME/.local/bin:\$PATH\""
        ;;
esac
log "Start ShareX with: sharex -OpenMainWindow"
log "Remove it again with: $libdir/uninstall.sh --uninstall"
