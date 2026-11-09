#!/bin/sh
# Point Luna's msbuildMac64 setting here. Runs msbuild-cache.js with Luna's bundled Node
# (Luna starts MSBuild with its install folder as the working directory), else any Node on PATH,
# else plain MSBuild.
HERE="$(cd "$(dirname "$0")" && pwd)"
# Real compiles go through msbuild-arm.sh (native Mono on Apple Silicon, else regular MSBuild).
export LUNA_REAL_MSBUILD="${LUNA_REAL_MSBUILD:-$HERE/msbuild-arm.sh}"
REAL_MSBUILD="$LUNA_REAL_MSBUILD"
SCRIPT="$HERE/msbuild-cache.js"
NODE="$PWD/tools/node/mac64/bin/node"
if [ ! -x "$NODE" ]; then
  NODE="$(command -v node)"
fi
if [ -z "$NODE" ] || [ ! -f "$SCRIPT" ]; then
  exec "$REAL_MSBUILD" "$@"
fi
exec "$NODE" "$SCRIPT" "$@"
