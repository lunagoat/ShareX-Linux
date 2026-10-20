#!/usr/bin/env bash
# Build the release tarball used by the curl installer (install.sh) and the GitHub release workflow.
#
#   scripts/linux/make-release.sh [--rid linux-x64] [--out DIR] [--no-publish]
#
# Output (default dist/release/):
#   sharex-linux-x64.tar.gz          self-contained build + installer + packaging files
#   sharex-linux-x64.tar.gz.sha256   checksum in `sha256sum -c` format
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
rid=linux-x64
out="$root/dist/release"
do_publish=1

while [ "$#" -gt 0 ]; do
    case "$1" in
        --rid) rid="$2"; shift 2 ;;
        --out) out="$2"; shift 2 ;;
        --no-publish) do_publish=0; shift ;;
        -h|--help) sed -n '2,10p' "${BASH_SOURCE[0]}"; exit 0 ;;
        *) echo "make-release.sh: unknown argument: $1" >&2; exit 2 ;;
    esac
done

app="$root/dist/$rid"
if [ "$do_publish" -eq 1 ] || [ ! -x "$app/ShareX" ]; then
    "$root/scripts/linux/publish.sh" --rid "$rid" >/dev/null
fi
[ -x "$app/ShareX" ] || { echo "make-release.sh: $app/ShareX is missing" >&2; exit 1; }

name="sharex-$rid"
stage="$(mktemp -d)"
trap 'rm -rf "$stage"' EXIT
top="$stage/$name"

# The layout mirrors the repository so scripts/linux/install-user.sh works unchanged.
mkdir -p "$top/dist/$rid" "$top/scripts/linux" "$top/packaging/linux" "$top/docs"
cp -a "$app/." "$top/dist/$rid/"
cp "$root/scripts/linux/install-user.sh" "$top/scripts/linux/"
cp -a "$root/packaging/linux/." "$top/packaging/linux/"
cp "$root/Directory.build.props" "$root/LICENSE.txt" "$root/README.linux.md" "$top/"
# The exact release (v21.0.0-linux.N) so `sharex --version` and `sharex --doctor` can tell builds apart.
git -C "$root" describe --tags --always 2>/dev/null > "$top/VERSION" || true
cp "$root/docs/CLI.md" "$top/docs/"
[ "$rid" = "linux-x64" ] || sed -i "s#dist/linux-x64#dist/$rid#g" "$top/scripts/linux/install-user.sh"

mkdir -p "$out"
tar -C "$stage" --owner=0 --group=0 --numeric-owner -czf "$out/$name.tar.gz" "$name"
( cd "$out" && sha256sum "$name.tar.gz" > "$name.tar.gz.sha256" )

echo "Release archive: $out/$name.tar.gz ($(du -h "$out/$name.tar.gz" | cut -f1))"
cat "$out/$name.tar.gz.sha256"
