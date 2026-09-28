# StreamingCamera

A Raspberry Pi webcam in one ASP.NET Core file.

```bash
dotnet run --project demo/StreamingCamera        # then open http://<your-pi>:5000
```

| Route | What it does |
|---|---|
| `GET /` | a page showing the live stream |
| `GET /live.mp4` | the camera as fragmented MP4 (H.264), which plays in a browser and in VLC |
| `GET /stream.mjpg` | the same picture as multipart MJPEG, for motionEye and Home Assistant |

Each route is one SDK call: `RecordToAsync` writes the video into the response until the viewer
closes the tab. The HTTP side — the routes, the multipart framing in `MultipartStream.cs`, the
hosting — is the application's, not the SDK's.
