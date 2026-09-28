#!/usr/bin/env bash
# Build the shims (normal + sanitized) and run the test suite in three modes inside the VM:
#   1. plain
#   2. forced GC + finalizers between every step (LIBCAMERASHARP_TEST_GC_STRESS) and a tiny gen0
#   3. AddressSanitizer + UBSan shim, with (2)
#
#   limactl shell libcamera -- /work/tests/vm/run-tests.sh
set -euo pipefail
cd /work

cmake -S src/LibcameraSharp.Native -B ~/build/shim -DCMAKE_BUILD_TYPE=Release >/dev/null
cmake --build ~/build/shim -j"$(nproc)" | grep -E "error|Built target" || true
cmake -S src/LibcameraSharp.Native -B ~/build/shim-asan -DCMAKE_BUILD_TYPE=Debug -DSHIM_SANITIZE=ON >/dev/null
cmake --build ~/build/shim-asan -j"$(nproc)" | grep -E "error|Built target" || true

src/LibcameraSharp.Native/check_symbols.sh ~/build/shim/libcamera-shim.so
src/LibcameraSharp.Native/check_symbols.sh ~/build/shim/libtiff-shim.so
src/LibcameraSharp.Native/check_symbols.sh ~/build/shim/libexif-shim.so

# The whole solution, so the scenario listings compile too.
dotnet build LibcameraSharp.slnx --nologo -v q

# The xUnit v3 test project is an executable; running it directly keeps LD_PRELOAD (ASan) to our
# process — under `dotnet test` it reaches the test platform's host too, which dies on trixie.
# Every mode prints its result, or why there is none, and the next mode still runs: a crash or a hang
# must not end the script silently. Five minutes a mode; the suite takes seconds.
TESTS=tests/LibcameraSharp.Tests/bin/Debug/net10.0/LibcameraSharp.Tests
failed=0
run() {
  local out rc=0
  out=$(timeout 300 "$TESTS" -noColor 2>&1) || rc=$?
  echo "$out" | grep -E "Total:|\[FAIL\]|AddressSanitizer" || echo "no result (exit $rc)"
  [ $rc -eq 124 ] && echo "timed out after 5 minutes"
  [ $rc -ne 0 ] && failed=1
  return 0
}

echo "== 1/3 plain"
LIBCAMERASHARP_SHIM=$HOME/build/shim/libcamera-shim.so LIBCAMERASHARP_TIFF_SHIM=$HOME/build/shim/libtiff-shim.so LIBCAMERASHARP_EXIF_SHIM=$HOME/build/shim/libexif-shim.so run

echo "== 2/3 GC stress"
DOTNET_GCgen0size=0x20000 LIBCAMERASHARP_TEST_GC_STRESS=1 \
LIBCAMERASHARP_SHIM=$HOME/build/shim/libcamera-shim.so LIBCAMERASHARP_TIFF_SHIM=$HOME/build/shim/libtiff-shim.so LIBCAMERASHARP_EXIF_SHIM=$HOME/build/shim/libexif-shim.so run

echo "== 3/3 ASan + UBSan + GC stress"
ASAN=$(gcc -print-file-name=libasan.so)
# .NET installs its own signal handlers and isn't leak-clean; tell ASan not to fight it.
LD_PRELOAD=$ASAN \
ASAN_OPTIONS=detect_leaks=0:verify_asan_link_order=0:handle_segv=0:handle_sigbus=0:handle_abort=0:handle_sigfpe=0:allow_user_segv_handler=1:use_sigaltstack=0 \
DOTNET_GCgen0size=0x20000 LIBCAMERASHARP_TEST_GC_STRESS=1 \
LIBCAMERASHARP_SHIM=$HOME/build/shim-asan/libcamera-shim.so LIBCAMERASHARP_TIFF_SHIM=$HOME/build/shim-asan/libtiff-shim.so LIBCAMERASHARP_EXIF_SHIM=$HOME/build/shim-asan/libexif-shim.so run

exit $failed
