#!/usr/bin/env bash
# Build the shims to ship: linux-arm64, Debian trixie, linked against Raspberry Pi OS's libcamera 0.7
# (the SONAME is per minor version, so any 0.7.x loads it), libtiff 6 and libexif 12. Output lands where the
# csprojs pack it:
#   src/LibcameraSharp.Core/runtimes/linux-arm64/native/libcamera-shim.so
#   src/LibcameraSharp/runtimes/linux-arm64/native/libtiff-shim.so
#   src/LibcameraSharp/runtimes/linux-arm64/native/libexif-shim.so
#
#   src/LibcameraSharp.Native/build-shim.sh
set -euo pipefail
cd "$(dirname "$0")/../.."

docker build --platform linux/arm64 -f src/LibcameraSharp.Native/Dockerfile.libcamera -t libcamerasharp-abi . >/dev/null
docker run --rm --platform linux/arm64 -v "$PWD:/work" libcamerasharp-abi bash -c '
  set -e
  cmake -S /work/src/LibcameraSharp.Native -B /tmp/shim -DCMAKE_BUILD_TYPE=Release >/dev/null
  cmake --build /tmp/shim -j"$(nproc)" | grep -E "error|Built target"
  strip --strip-unneeded /tmp/shim/libcamera-shim.so
  install -D /tmp/shim/libcamera-shim.so /work/src/LibcameraSharp.Core/runtimes/linux-arm64/native/libcamera-shim.so
  echo "built against: $(pkg-config --modversion libcamera) ($(dpkg -s libcamera0.7 | grep ^Version))"
  ldd /work/src/LibcameraSharp.Core/runtimes/linux-arm64/native/libcamera-shim.so | grep -E "libcamera|libstdc|libc\.so"
  strip --strip-unneeded /tmp/shim/libtiff-shim.so
  install -D /tmp/shim/libtiff-shim.so /work/src/LibcameraSharp/runtimes/linux-arm64/native/libtiff-shim.so
  echo "built against: libtiff $(pkg-config --modversion libtiff-4)"
  ldd /work/src/LibcameraSharp/runtimes/linux-arm64/native/libtiff-shim.so | grep -E "libtiff|libc\.so"
  strip --strip-unneeded /tmp/shim/libexif-shim.so
  install -D /tmp/shim/libexif-shim.so /work/src/LibcameraSharp/runtimes/linux-arm64/native/libexif-shim.so
  echo "built against: libexif $(pkg-config --modversion libexif)"
  ldd /work/src/LibcameraSharp/runtimes/linux-arm64/native/libexif-shim.so | grep -E "libexif|libc\.so"
'
src/LibcameraSharp.Native/check_symbols.sh src/LibcameraSharp.Core/runtimes/linux-arm64/native/libcamera-shim.so
src/LibcameraSharp.Native/check_symbols.sh src/LibcameraSharp/runtimes/linux-arm64/native/libtiff-shim.so
src/LibcameraSharp.Native/check_symbols.sh src/LibcameraSharp/runtimes/linux-arm64/native/libexif-shim.so
ls -la src/LibcameraSharp.Core/runtimes/linux-arm64/native/ src/LibcameraSharp/runtimes/linux-arm64/native/
