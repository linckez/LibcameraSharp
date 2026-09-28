#!/usr/bin/env bash
# Fail if a shim's exported symbols and the C# imports for it differ in any way.
#   src/LibcameraSharp.Native/check_symbols.sh path/to/libcamera-shim.so   # against the generated symbols.txt
#   src/LibcameraSharp.Native/check_symbols.sh path/to/libtiff-shim.so     # against Photos/Interop/Tiff/symbols.txt
#   src/LibcameraSharp.Native/check_symbols.sh path/to/libexif-shim.so     # against Photos/Interop/Exif/symbols.txt
set -euo pipefail
so="${1:?path to a shim: libcamera-shim.so, libtiff-shim.so or libexif-shim.so}"
here="$(cd "$(dirname "$0")/../.." && pwd)"
case "$(basename "$so")" in
  libcamera-shim.so)
    prefix=libcamera_
    imports="$(sort "$here/src/LibcameraSharp.Core/Native/Interop/symbols.txt")" ;;
  libtiff-shim.so)
    prefix=tiff_
    imports="$(sort "$here/src/LibcameraSharp/Photos/Interop/Tiff/symbols.txt")" ;;
  libexif-shim.so)
    prefix=exif_block_
    imports="$(sort "$here/src/LibcameraSharp/Photos/Interop/Exif/symbols.txt")" ;;
  *) echo "unknown shim: $so" >&2; exit 2 ;;
esac
exports="$(nm -D --defined-only "$so" | awk '$2 == "T" {print $3}' | grep "^$prefix" | sort)"
if diff <(echo "$exports") <(echo "$imports") >/dev/null; then
  echo "ok: $(basename "$so"): $(echo "$exports" | wc -l | tr -d ' ') symbols, exports == imports"
else
  echo "MISMATCH between $(basename "$so") exports (<) and C# imports (>):" >&2
  diff <(echo "$exports") <(echo "$imports") >&2 || true
  exit 1
fi
