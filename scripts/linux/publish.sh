#!/usr/bin/env bash
# Publish a self-contained, single-file Linux build of ShareX.
#
#   scripts/linux/publish.sh [--rid linux-x64] [--out DIR] [--symbols]
#
# The result is a fully self-contained application: the target machine does not
# need the .NET runtime. Default output directory: dist/<rid>/ (git-ignored).
#
# The publish profile lives in ShareX/Properties/PublishProfiles/linux-x64.pubxml
# and is also used by packaging/arch/PKGBUILD and packaging/flatpak/.
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
source "$root/scripts/linux/env.sh"

rid=linux-x64
out=""
symbols=0

while [ "$#" -gt 0 ]; do
    case "$1" in
        --rid) rid="$2"; shift 2 ;;
        --out|-o) out="$2"; shift 2 ;;
        --symbols) symbols=1; shift ;;
        -h|--help)
            sed -n '2,9p' "${BASH_SOURCE[0]}"
            exit 0
            ;;
        *) echo "publish.sh: unknown argument: $1" >&2; exit 2 ;;
    esac
done

out="${out:-$root/dist/$rid}"
mkdir -p "$out"

publish_args=(
    "$root/ShareX/ShareX.csproj"
    -c Release
    -r "$rid"
    -p:PublishProfile=linux-x64
    -p:PublishDir="$out/"
    -nologo
)

if [ "$symbols" -eq 0 ]; then
    # The profile only applies to the main project, so keep the referenced
    # projects from dropping portable PDBs into the publish folder as well.
    publish_args+=(-p:DebugType=none -p:DebugSymbols=false)
fi

echo "Publishing ShareX ($rid) to $out"
dotnet publish "${publish_args[@]}" "$@"

echo
echo "Published:"
if [ -x "$out/ShareX" ]; then
    printf '  %s (%s)\n' "$out/ShareX" "$(du -h "$out/ShareX" | cut -f1)"
fi
echo
echo "Run it with:  $out/ShareX"
echo "Install to ~/.local with:  scripts/linux/install-user.sh"
