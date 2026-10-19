#!/usr/bin/env bash
# Build the whole app. Usage: scripts/linux/build.sh [dotnet build args]
set -e
cd "$(dirname "$0")/../.."
source scripts/linux/env.sh
dotnet build ShareX/ShareX.csproj -nologo -v q "$@" 2>&1 | grep -E "error|warning CS8|Build succeeded|Error\(s\)" | sed 's/\[\/.*//' | sort -u | head -80
