#!/usr/bin/env bash
# Exercise the build wrapper with a fake SDK; no publish, installation or display access.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d /tmp/sharex-script-bugs.XXXXXX)"
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/scripts/linux" "$work/bin"
cp "$root/scripts/linux/build.sh" "$work/scripts/linux/build.sh"
# Keep the fixture SDK on PATH instead of selecting a real user-local SDK.
: > "$work/scripts/linux/env.sh"
cat > "$work/bin/dotnet" <<'SDK'
#!/usr/bin/env bash
printf 'error: simulated compiler failure\n'
exit 42
SDK
chmod +x "$work/bin/dotnet"
status=0
PATH="$work/bin:$PATH" bash "$work/scripts/linux/build.sh" > "$work/output" 2>&1 || status=$?
if [ "$status" -ne 42 ]; then
    printf 'FAIL: build wrapper returned %s instead of compiler exit 42\n' "$status" >&2
    exit 1
fi
cat > "$work/bin/dotnet" <<'SDK'
#!/usr/bin/env bash
printf 'Build succeeded.\n    0 Error(s)\n'
SDK
PATH="$work/bin:$PATH" bash "$work/scripts/linux/build.sh" > "$work/output" 2>&1
# The filter may find no lines or truncate a long warning list; neither is a compiler failure.
cat > "$work/bin/dotnet" <<'SDK'
#!/usr/bin/env bash
exit 0
SDK
PATH="$work/bin:$PATH" bash "$work/scripts/linux/build.sh" > "$work/output" 2>&1
cat > "$work/bin/dotnet" <<'SDK'
#!/usr/bin/env bash
for ((i=0; i<10000; i++)); do printf 'warning CS8000: %08d\n' "$i"; done
SDK
PATH="$work/bin:$PATH" bash "$work/scripts/linux/build.sh" > "$work/output" 2>&1
printf 'PASS: build wrapper propagates compiler failure and accepts success, empty output and truncated warnings\n'
