#!/usr/bin/env bash
# Regenerate every generated C# file. Run inside the test VM (needs libclang's Python bindings
# and installed libcamera, libtiff and libexif headers for `pkg-config --cflags`):
#
#   limactl shell libcamera -- /work/src/LibcameraSharp.Core/Generate/regenerate.sh
#
# CI runs this and fails on any resulting diff, so generated files can never go stale.
set -euo pipefail
cd "$(dirname "$0")"

python3 gen_pinvoke.py      # src/LibcameraSharp.Native/c_api/*.h -> ../Native/Interop/*.g.cs
python3 gen_pinvoke.py --api-dir ../../LibcameraSharp.Native/tiff --out-dir ../../LibcameraSharp/Photos/Interop/Tiff \
    --library libtiff-shim --namespace LibcameraSharp.Photos.Interop.Tiff --cflags "$(pkg-config --cflags libtiff-4)"
python3 gen_pinvoke.py --api-dir ../../LibcameraSharp.Native/exif --out-dir ../../LibcameraSharp/Photos/Interop/Exif \
    --library libexif-shim --namespace LibcameraSharp.Photos.Interop.Exif --cflags "$(pkg-config --cflags libexif)"
python3 gen_controls.py     # inputs/*.yaml                      -> ../{Controls,Formats,Logging}/*.g.cs
python3 gen_bayer.py        # inputs/*/bayer_format.cpp          -> ../Formats/BayerFormats.g.cs
