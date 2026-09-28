# Raspberry Pi setup

LibcameraSharp uses the libcamera installed on the Pi. Get the camera working in the Pi's own camera
tools first; then the SDK sees exactly what they see.

## Check the camera

Raspberry Pi OS ships libcamera. With the camera connected:

```bash
rpicam-hello --list-cameras
```

If your camera is listed, you're done. If not, the problem is the driver, the cable or the tuning,
not your .NET code.

Other camera stacks work the same way: once libcamera lists the camera, LibcameraSharp can use it.
The binding targets libcamera 0.7; a different minor version still runs, with a warning on stderr.

## Deploying a .NET app

Self-contained, for the Pi, from any machine:

```bash
dotnet publish -c Release -r linux-arm64 --self-contained -o out/pi
scp -r out/pi pi@raspberrypi.local:~/app
```

The native shims are found automatically next to the app. `LIBCAMERASHARP_SHIM=/path/to/libcamera-shim.so`
points at one you built yourself, for a libcamera the packaged one doesn't match; `LIBCAMERASHARP_TIFF_SHIM`
and `LIBCAMERASHARP_EXIF_SHIM` do the same for the ones that write DNG files and EXIF.

## Permissions

The user running the app needs `/dev/media*` and `/dev/video*`; on Raspberry Pi OS the default user
is in the `video` group already. In a container, pass the devices through and add the group.

## Memory for large sensors

A 21 MP photo in BGR888 is 64 MB, and libcamera allocates its buffers from CMA. If opening a stream
fails with `ENOMEM`, raise the CMA size in `/boot/firmware/config.txt`
(`dtoverlay=vc4-kms-v3d,cma-512` or similar).

## Pi 4 and Pi 5

Both work with the same code. The difference you'll notice is video: a Pi 4 encodes H.264 in
hardware, up to 1080p30; a Pi 5 encodes in software, using about 1½ cores at 1080p30.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| `CameraBusyException` | another process holds the camera: a camera app, a browser, or a service you forgot |
| No cameras, but `rpicam-hello` lists one | the app loads a shim built for another libcamera; check `CameraManager.Version` |
| "running libcamera 0.8.x, but these bindings support 0.7.x" on stderr | libcamera was upgraded; it runs, but wait for a matching package before trusting every control |
| "The FFmpeg libraries could not be loaded" | H.264 and `.mp4` need `libavcodec` and `libavformat`: `sudo apt install ffmpeg` |
| Colours swapped | byte order — see [Frames](frames.md#formats-and-planes) |
