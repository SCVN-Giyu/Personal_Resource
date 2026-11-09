#!/bin/sh
# Stands in for Luna's brotli via BROTLI_PATH_OVERRIDE during dashboard measurements.
# Luna calls: brotli -Z -f <file>  (writes <file>.br). Runs Luna's own brotli binary, but reuses the
# output when the same input was compressed before. $LUNA_REAL_BROTLI is Luna's binary; $LUNA_BROTLI_CACHE the cache folder.
INPUT=""
OPTIONS=""
for arg in "$@"; do OPTIONS="$OPTIONS $INPUT"; INPUT="$arg"; done
if [ -z "$LUNA_REAL_BROTLI" ] || [ -z "$LUNA_BROTLI_CACHE" ] || [ ! -f "$INPUT" ]; then
  exec "${LUNA_REAL_BROTLI:-brotli}" "$@"
fi
# Key on content + options only: Luna passes a random temp file name each time.
KEY="$(/sbin/md5 -q "$INPUT")-$(echo "$OPTIONS" | /sbin/md5 -q)"
CACHED="$LUNA_BROTLI_CACHE/$KEY.br"
# On a hit, reuse the cached output; if that copy fails for any reason, compress for real instead.
if [ -f "$CACHED" ] && cp "$CACHED" "$INPUT.br"; then
  exit 0
fi
"$LUNA_REAL_BROTLI" "$@" || exit $?
mkdir -p "$LUNA_BROTLI_CACHE"
cp "$INPUT.br" "$CACHED.$$" && mv "$CACHED.$$" "$CACHED"
exit 0
