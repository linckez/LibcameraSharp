#!/usr/bin/env bash
# Build and install the Raspberry Pi libcamera fork inside the Lima VM.
#
#   limactl shell libcamera -- /work/tests/vm/setup-libcamera.sh [TAG]
#
# Pipelines: virtual (userspace: NV12, several cameras, frames
# from image files) + rpi/vc4 (so control_ids_rpi.yaml is part of the generated headers, matching
# what Pi OS ships).
# Builds out of the shared mount for speed; installs to /usr/local.
set -euo pipefail

TAG="${1:-v0.7.2+rpt20260817}"          # libcamera0.7 0.7.2+rpt20260817-1 on Pi OS trixie
HERE="$(cd "$(dirname "$0")" && pwd)"
SRC="$HOME/src/libcamera"
REPO="https://github.com/raspberrypi/libcamera"

if [ ! -d "$SRC/.git" ]; then
  git clone --depth 1 --branch "$TAG" "$REPO" "$SRC"
else
  git -C "$SRC" fetch --depth 1 origin "refs/tags/$TAG:refs/tags/$TAG"
  git -C "$SRC" checkout -q "$TAG"
fi
git -C "$SRC" describe --tags --always

cd "$SRC"
meson setup build --reconfigure --buildtype=release \
  -Dpipelines=virtual,rpi/vc4 -Dipas=rpi/vc4 -Drpi-awb-nn=disabled \
  -Dcam=enabled -Dqcam=disabled -Dgstreamer=disabled -Dpycamera=disabled \
  -Dlc-compliance=disabled -Ddocumentation=disabled -Dtest=false \
  -Dv4l2=false
ninja -C build
sudo ninja -C build install
sudo ldconfig

# The virtual pipeline reads its cameras from this file: one test-pattern camera and one that
# plays a known image, so captures can be compared pixel for pixel.
sudo install -D -m 644 "$HERE/virtual.yaml" /usr/local/share/libcamera/pipeline/virtual/virtual.yaml
sudo install -D -m 644 "$HERE/virtual-frame.jpg" /usr/local/share/libcamera/pipeline/virtual/frames/0.jpg

echo
echo "installed: $(pkg-config --modversion libcamera) at $(pkg-config --variable=libdir libcamera)"
echo "cameras:"
cam -l 2>/dev/null | sed -n '/Available/,$p'
