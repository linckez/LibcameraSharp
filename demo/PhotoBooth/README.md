# PhotoBooth

The SDK in one small console app: list the cameras, see what one can do, watch a few frames, take a
full-resolution photo as JPEG and DNG, then lock the exposure to what that photo used and take another.

```bash
dotnet run --project demo/PhotoBooth                 # first camera, files in ./photos
dotnet run --project demo/PhotoBooth -- 1 ~/shots    # camera number, output folder
```

Run it on a Pi whose camera shows up in `rpicam-hello --list-cameras`. Cameras without a raw stream
skip the DNG; cameras that report no exposure skip the lock.

Read [Program.cs](Program.cs) top to bottom; each step is one or two SDK calls.
