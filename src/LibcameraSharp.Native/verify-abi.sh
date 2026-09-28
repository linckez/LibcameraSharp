#!/usr/bin/env bash
# Prove the generated tables and struct layouts match an installed libcamera at runtime: build the
# shims against its headers (and libtiff and libexif), check generated code is current, run every test that needs no camera.
#
#   src/LibcameraSharp.Native/verify-abi.sh              # Raspberry Pi OS's libcamera (the primary target)
set -euo pipefail
cd "$(dirname "$0")/../.."
IMAGE="libcamerasharp-abi"

docker build --platform linux/arm64 -f src/LibcameraSharp.Native/Dockerfile.libcamera -t "$IMAGE" . >/dev/null
# Work on a copy so the container's bin/obj never mix with the host's or the VM's.
docker run --rm --platform linux/arm64 -v "$PWD:/work:ro" -e HOME=/tmp "$IMAGE" bash -c '
  set -e
  rsync -a --exclude bin --exclude obj --exclude research /work/ /tmp/src/
  cd /tmp/src
  cmake -S src/LibcameraSharp.Native -B /tmp/shim -DCMAKE_BUILD_TYPE=Release >/dev/null && cmake --build /tmp/shim -j"$(nproc)" >/dev/null
  src/LibcameraSharp.Native/check_symbols.sh /tmp/shim/libcamera-shim.so
  src/LibcameraSharp.Native/check_symbols.sh /tmp/shim/libtiff-shim.so
  src/LibcameraSharp.Native/check_symbols.sh /tmp/shim/libexif-shim.so
  echo "libcamera: $(dpkg -s libcamera0.7 | grep ^Version)"
  # Generated code must be current: stage the tree as copied, regenerate against this libcamera, expect no diff.
  git -c safe.directory=/tmp/src add -A . >/dev/null
  src/LibcameraSharp.Core/Generate/regenerate.sh >/dev/null
  git -c safe.directory=/tmp/src diff --exit-code --stat -- src || { echo "::error::generated files are stale; run src/LibcameraSharp.Core/Generate/regenerate.sh"; exit 1; }
  dotnet build tests/LibcameraSharp.Scenarios --nologo -v q 2>&1 | grep -E " error |Build succeeded"
  LIBCAMERASHARP_SHIM=/tmp/shim/libcamera-shim.so LIBCAMERASHARP_TIFF_SHIM=/tmp/shim/libtiff-shim.so LIBCAMERASHARP_EXIF_SHIM=/tmp/shim/libexif-shim.so dotnet test tests/LibcameraSharp.Tests --nologo -v q \
      --filter "FullyQualifiedName~ControlTable|FullyQualifiedName~Layout|FullyQualifiedName~Version_string|FullyQualifiedName~Patching|FullyQualifiedName~DngWriter|FullyQualifiedName~ExifSegment" 2>&1 \
      | grep -E "Passed!|Failed!|error|Aborted"
'
