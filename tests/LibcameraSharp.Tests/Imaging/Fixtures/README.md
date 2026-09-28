# Fixtures

Expected output made by other, widely used software from the same input, so the tests compare our
bytes against an independent answer rather than against our own earlier output.

| Files | Input | Expected output from |
|---|---|---|
| `dng.json`, `dng.raw.bin` → `dng.reference.dng` | a synthetic 64×32 SRGGB10 CSI-2 packed frame and its metadata | PiDNG (`PICAM2DNG`) |
| `exif.json` → `exif.default.bin`, `exif.custom.bin` | fixed metadata, once with a user Model tag | picamera2 (`Helpers._prepare_exif`, via piexif) |
| `raw_unpacking.json`, `*.packed.bin` → `*.unpacked.bin` | seeded random buffers, 44×6 with row padding | PiDNG (CSI-2 and unpacked formats), picamera2 (`Helpers.decompress`, PiSP mode 1) |

The inputs are deterministic (seed `20260922`), so the files only change if the expected behaviour does.
