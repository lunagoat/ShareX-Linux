# source this INSTEAD of env.sh when the shell is sandboxed (only the checkout itself is writable).
# Keeps every cache/temp file the .NET toolchain writes inside the checkout (.cache/, git-ignored).
root="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")/../.." && pwd)"
source "$root/scripts/linux/env.sh"
mkdir -p "$root/.cache"/{nuget,dotnet-home,tmp,nuget-http}
if [ ! -e "$root/.cache/nuget/.seeded" ]; then
  cp -al "$HOME/.nuget/packages/." "$root/.cache/nuget/" 2>/dev/null || cp -a "$HOME/.nuget/packages/." "$root/.cache/nuget/"
  touch "$root/.cache/nuget/.seeded"
fi
export NUGET_PACKAGES="$root/.cache/nuget" NUGET_HTTP_CACHE_PATH="$root/.cache/nuget-http" DOTNET_CLI_HOME="$root/.cache/dotnet-home" TMPDIR="$root/.cache/tmp"
export DOTNET_gcServer=0 MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0 UseSharedCompilation=false
