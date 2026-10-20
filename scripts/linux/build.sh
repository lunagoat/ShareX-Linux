#!/usr/bin/env bash
# Build the whole app. Usage: scripts/linux/build.sh [dotnet build args]
set -eo pipefail
cd "$(dirname "$0")/../.."
source scripts/linux/env.sh
# Preserve the compiler status even if grep finds nothing or head truncates the filtered output.
dotnet build ShareX/ShareX.csproj -nologo -v q "$@" 2>&1 | grep -E "error|warning CS8|Build succeeded|Error\(s\)" | sed 's/\[\/.*//' | sort -u | head -80 || exit "${PIPESTATUS[0]}"
