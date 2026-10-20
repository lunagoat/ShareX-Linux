#!/usr/bin/env bash
# Cut a release: tag the current commit and let GitHub Actions build and publish it, then verify what users will download.
#
#   scripts/linux/release.sh v21.0.0-linux.11
#
# Needs gh (logged in). Write release notes in release-notes/<tag>.md first (otherwise GitHub generates them).
set -euo pipefail

tag="${1:?usage: release.sh <tag>  (for example v21.0.0-linux.11)}"
repo="${SHAREX_REPO:-lunagoat/ShareX-Linux}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$root"

[ -z "$(git status --porcelain)" ] || { echo "release.sh: the working tree has uncommitted changes" >&2; exit 1; }
git rev-parse "$tag" >/dev/null 2>&1 && { echo "release.sh: tag $tag already exists" >&2; exit 1; }

git push origin HEAD:main
git tag -a "$tag" -m "$tag"
git push origin "$tag"

echo "Waiting for the Release workflow ..."
sleep 8
run_id=""
for _ in $(seq 1 30); do
    run_id="$(gh run list --repo "$repo" --workflow Release --branch "$tag" --limit 1 --json databaseId --jq '.[0].databaseId' 2>/dev/null || true)"
    [ -n "$run_id" ] && break
    sleep 4
done
[ -n "$run_id" ] || { echo "release.sh: the Release workflow did not start" >&2; exit 1; }
gh run watch "$run_id" --repo "$repo" --exit-status

echo "Verifying the published release ..."
tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
base="https://github.com/$repo/releases/download/$tag"
curl -fsSL -o "$tmp/sharex-linux-x64.tar.gz" "$base/sharex-linux-x64.tar.gz"
curl -fsSL -o "$tmp/sharex-linux-x64.tar.gz.sha256" "$base/sharex-linux-x64.tar.gz.sha256"
( cd "$tmp" && sha256sum -c sharex-linux-x64.tar.gz.sha256 )
echo "VERSION in the archive: $(tar -xzOf "$tmp/sharex-linux-x64.tar.gz" sharex-linux-x64/VERSION)"
echo "Release $tag is published and verified: https://github.com/$repo/releases/tag/$tag"
