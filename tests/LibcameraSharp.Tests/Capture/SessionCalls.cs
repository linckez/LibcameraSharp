namespace LibcameraSharp.Tests.Capture;

/// <summary>
/// Calls for tests that drive a <see cref="CameraSession"/> directly rather than through a <see cref="CameraDevice"/>:
/// each runs on the session's loop, as the device's own calls do.
/// </summary>
internal static class SessionCalls
{
    extension(CameraSession session)
    {
        /// <summary>Sets the camera up with the configuration <paramref name="make"/> builds.</summary>
        public Task ConfigureAsync(Func<CameraSession, SessionConfiguration> make) => session.CallAsync(() => session.Configure(make(session)));

        /// <summary>Starts the configured camera.</summary>
        public Task StartAsync() => session.CallAsync(session.Start);

        /// <summary>Stops the camera; captures still waiting fail with <see cref="OperationCanceledException"/>.</summary>
        public Task StopAsync() => session.CallAsync(() => session.Stop());

        /// <summary>Records the configured camera into <paramref name="output"/> with <paramref name="encoder"/>; attaching starts the camera.</summary>
        public async Task StartRecordingAsync(Encoder encoder, Output output, Quality? quality = null)
        {
            var (configuration, frameRate) = await session.CallAsync(() => (session.CameraConfiguration!, session.NominalFrameRate()));
            CameraSession.PrepareEncoder(encoder, configuration, frameRate);
            encoder.Output = output;
            encoder.Start(quality);
            await session.AttachEncoderAsync(encoder);
        }

        /// <summary>Detaches <paramref name="encoder"/> (the last recording stops the camera), then flushes and closes it.</summary>
        public async Task StopRecordingAsync(Encoder encoder)
        {
            await session.DetachEncoderAsync(encoder);
            await Task.Run(encoder.Stop);
        }
    }
}
