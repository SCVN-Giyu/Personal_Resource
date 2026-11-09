#!/bin/sh
# Runs Mono's MSBuild on a native Apple Silicon Mono (Homebrew) instead of the Intel-only
# Mono.framework, which runs under Rosetta. Same MSBuild, same compiler, same output; ~2x faster.
# Falls back to the regular MSBuild when no native Mono is installed.
# hw.optional.arm64 rather than uname: Luna starts this from its Intel Node, so uname reports x86_64 under Rosetta.
INTEL_MSBUILD=/Library/Frameworks/Mono.framework/Versions/Current/bin/msbuild
MSBUILD_DLL=/Library/Frameworks/Mono.framework/Versions/Current/lib/mono/msbuild/15.0/bin/MSBuild.dll
ARM_MONO=/opt/homebrew/bin/mono
if [ "$(sysctl -n hw.optional.arm64 2>/dev/null)" = "1" ] && [ -x "$ARM_MONO" ] && [ -f "$MSBUILD_DLL" ]; then
  MONO_GC_PARAMS="nursery-size=64m,$MONO_GC_PARAMS" exec "$ARM_MONO" --assembly-loader=strict $MONO_OPTIONS "$MSBUILD_DLL" "$@"
fi
exec "$INTEL_MSBUILD" "$@"
