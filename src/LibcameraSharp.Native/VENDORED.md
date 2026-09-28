# Vendored native shim

`c_api/` is copied **verbatim** from
[lit-robotics/libcamera-rs](https://github.com/lit-robotics/libcamera-rs)
`libcamera-sys/c_api` (MIT OR Apache-2.0).

| | |
|---|---|
| Upstream commit | `ac0222083b23da70bfe27fcf3696b2f487a11df2` (2026-02-25) |
| Upstream libcamera target | 0.7.0 (CI matrix 0.4–0.7) |
| Built and verified here against | libcamera 0.7.2+rpt20260817 (Raspberry Pi fork) |

## Rules

- Do not edit files under `c_api/`. To update: copy from upstream again, bump the
  table above, run `src/LibcameraSharp.Core/Generate/regenerate.sh` (in the VM), and rebuild — the C# P/Invoke layer is
  generated from these headers, so any drift shows up as a build/test failure.
- Local patches, if ever unavoidable, go in `patches/*.patch` and are listed here.

## Local patches

None.
