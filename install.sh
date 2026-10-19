#!/usr/bin/env bash
# ShareX for Linux - one-line installer (per-user, no sudo).
#
#   curl -fsSL https://raw.githubusercontent.com/lunagoat/ShareX-Linux/main/install.sh | bash
#
# Options (pass after `bash -s --` when piping):
#   --version TAG    install a specific release tag (default: the latest release)
#   --from-source    build from source instead of downloading a release (needs git; fetches a
#                    user-local .NET 10 SDK into ~/.dotnet when no suitable SDK is found)
#   --ref REF        git branch/tag used with --from-source (default: main)
#   --tarball FILE   install from a local release tarball (offline / testing)
#   --uninstall      remove ShareX from ~/.local again (settings are kept)
#   --quiet          print only errors
#   -h, --help       show this help
#
# Environment: SHAREX_REPO (default lunagoat/ShareX-Linux), SHAREX_BASE_URL (override the download base).
#
# Installs into ~/.local (bin/sharex, lib/sharex, share/applications, share/icons, share/mime).
# Nothing outside your home directory is touched; missing helper programs are only reported.
set -euo pipefail

REPO="${SHAREX_REPO:-lunagoat/ShareX-Linux}"
ASSET="sharex-linux-x64.tar.gz"
version=""
from_source=0
ref="main"
tarball=""
uninstall=0
quiet=0
qflag=()

log() { [ "$quiet" -eq 1 ] || printf '%s\n' "$*"; }
die() { printf 'install.sh: %s\n' "$*" >&2; exit 1; }

while [ "$#" -gt 0 ]; do
    case "$1" in
        --version) [ "$#" -ge 2 ] || die "--version needs a value"; version="$2"; shift 2 ;;
        --from-source) from_source=1; shift ;;
        --ref) [ "$#" -ge 2 ] || die "--ref needs a value"; ref="$2"; shift 2 ;;
        --tarball) [ "$#" -ge 2 ] || die "--tarball needs a value"; tarball="$2"; shift 2 ;;
        --uninstall) uninstall=1; shift ;;
        --quiet) quiet=1; qflag=(--quiet); shift ;;
        -h|--help) sed -n '2,19p' "$0" 2>/dev/null || true; exit 0 ;;
        *) die "unknown argument: $1 (try --help)" ;;
    esac
done

[ "$(id -u)" -ne 0 ] || die "do not run this as root; it installs for the current user only"
[ "$(uname -s)" = "Linux" ] || die "ShareX for Linux only runs on Linux"

need() { command -v "$1" >/dev/null 2>&1 || die "'$1' is required but was not found"; }

libdir="$HOME/.local/lib/sharex"

if [ "$uninstall" -eq 1 ]; then
    if [ -x "$libdir/uninstall.sh" ]; then
        exec "$libdir/uninstall.sh" --uninstall "${qflag[@]}"
    fi
    die "no installation found in $libdir"
fi

work="$(mktemp -d "${TMPDIR:-/tmp}/sharex-install.XXXXXX")"
trap 'rm -rf "$work"' EXIT

hotkey_hint() {
    case "${XDG_CURRENT_DESKTOP:-}${XDG_SESSION_DESKTOP:-}" in
        *yprland*|*sway*)
            log
            log "Hotkeys: your compositor does not bind keys by itself. Print ready-made lines with:"
            log "  sharex --print-binds hyprland     (or: hyprland-conf, sway)"
            log "and add them to your compositor config." ;;
    esac
}

report_missing_tools() {
    local missing=() tool
    command -v grim >/dev/null 2>&1 || command -v gnome-screenshot >/dev/null 2>&1 || [ "${XDG_SESSION_TYPE:-}" = "x11" ] || missing+=(grim)
    command -v slurp >/dev/null 2>&1 || [ "${XDG_SESSION_TYPE:-}" != "wayland" ] || missing+=(slurp)
    [ "${XDG_SESSION_TYPE:-}" != "wayland" ] || command -v wl-copy >/dev/null 2>&1 || missing+=(wl-clipboard)
    for tool in ffmpeg tesseract notify-send xdg-open; do
        command -v "$tool" >/dev/null 2>&1 || missing+=("$tool")
    done
    [ "${#missing[@]}" -gt 0 ] || return 0

    local id="" like="" pkgs="${missing[*]}"
    if [ -r /etc/os-release ]; then
        # shellcheck disable=SC1091
        id="$(. /etc/os-release; echo "${ID:-}")"; like="$(. /etc/os-release; echo "${ID_LIKE:-}")"
    fi
    log
    log "Optional helpers that are not installed yet (features that need them will say so): $pkgs"
    case " $id $like " in
        *" arch "*|*" cachyos "*|*" manjaro "*)
            pkgs="${pkgs//notify-send/libnotify}"; pkgs="${pkgs//xdg-open/xdg-utils}"
            log "  sudo pacman -S --needed $pkgs" ;;
        *" debian "*|*" ubuntu "*)
            pkgs="${pkgs//notify-send/libnotify-bin}"; pkgs="${pkgs//xdg-open/xdg-utils}"
            log "  sudo apt install $pkgs" ;;
        *" fedora "*|*" rhel "*)
            pkgs="${pkgs//notify-send/libnotify}"; pkgs="${pkgs//xdg-open/xdg-utils}"
            log "  sudo dnf install $pkgs" ;;
        *" suse "*|*" opensuse "*)
            pkgs="${pkgs//notify-send/libnotify-tools}"; pkgs="${pkgs//xdg-open/xdg-utils}"
            log "  sudo zypper install $pkgs" ;;
        *) log "  (install them with your distribution's package manager)" ;;
    esac
}

install_from_dir() {
    # $1: extracted release directory (contains scripts/linux/install-user.sh)
    local dir="$1"
    [ -x "$dir/scripts/linux/install-user.sh" ] || die "the archive does not contain the installer"
    "$dir/scripts/linux/install-user.sh" --no-publish "${qflag[@]}"
}

build_from_source() {
    need git
    need curl
    if ! { command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; } && [ ! -x "$HOME/.dotnet/dotnet" ]; then
        log "No .NET 10 SDK found; installing a user-local one into ~/.dotnet ..."
        curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$work/dotnet-install.sh"
        bash "$work/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet" >/dev/null
    fi
    log "Cloning https://github.com/$REPO ($ref) ..."
    git clone --quiet --depth 1 --branch "$ref" "https://github.com/$REPO.git" "$work/src"
    log "Building (this takes a few minutes) ..."
    "$work/src/scripts/linux/install-user.sh" "${qflag[@]}"
    report_missing_tools
    hotkey_hint
}

if [ "$from_source" -eq 1 ]; then
    build_from_source
    exit 0
fi

if [ "$(uname -m)" != "x86_64" ]; then
    log "Prebuilt releases are x86-64 only; building from source for $(uname -m)."
    build_from_source
    exit 0
fi

need tar
need sha256sum

if [ -n "$tarball" ]; then
    [ -r "$tarball" ] || die "cannot read $tarball"
    cp "$tarball" "$work/$ASSET"
    [ ! -r "$tarball.sha256" ] || cp "$tarball.sha256" "$work/$ASSET.sha256"
else
    need curl
    if [ -n "${SHAREX_BASE_URL:-}" ]; then
        base="$SHAREX_BASE_URL"
    elif [ -n "$version" ]; then
        base="https://github.com/$REPO/releases/download/$version"
    else
        base="https://github.com/$REPO/releases/latest/download"
    fi
    log "Downloading $base/$ASSET"
    if ! curl -fL --retry 3 --progress-bar -o "$work/$ASSET" "$base/$ASSET"; then
        [ -z "$version" ] && [ -z "${SHAREX_BASE_URL:-}" ] || die "download failed: $base/$ASSET"
        log "No prebuilt release is published yet - building from source instead."
        build_from_source
        exit 0
    fi
    curl -fsSL -o "$work/$ASSET.sha256" "$base/$ASSET.sha256" || rm -f "$work/$ASSET.sha256"
fi

if [ -r "$work/$ASSET.sha256" ]; then
    ( cd "$work" && sha256sum -c "$ASSET.sha256" >/dev/null ) || die "checksum mismatch - refusing to install"
    log "Checksum OK"
else
    log "Warning: no checksum file available, skipping verification"
fi

tar -C "$work" -xzf "$work/$ASSET"
install_from_dir "$work/sharex-linux-x64"
report_missing_tools
hotkey_hint
