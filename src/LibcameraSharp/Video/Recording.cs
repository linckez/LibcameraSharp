using System.Runtime.CompilerServices;

namespace LibcameraSharp;

/// <summary>
/// Attaches an encoder to a running camera, feeds it every frame of its capture stream, and detaches it.
/// </summary>
internal static class Recording
{
    private static readonly ConditionalWeakTable<CameraSession, HashSet<Encoder>> Encoders = [];

    extension(CameraSession session)
    {
        /// <summary>The encoders currently attached to this camera.</summary>
        public IReadOnlyCollection<Encoder> Encoders
        {
            get
            {
                var encoders = Recording.Encoders.GetOrCreateValue(session);
                lock (encoders)
                    return [.. encoders];
            }
        }

        /// <summary>Attaches <paramref name="encoder"/> to <paramref name="output"/> and starts the configured camera.</summary>
        public void StartRecording(Encoder encoder, Output output, Quality? quality = null)
        {
            session.StartEncoder(encoder, output, quality);
            session.Start();
        }

        /// <summary>Stops the camera, then flushes and closes every encoder and output.</summary>
        public void StopRecording()
        {
            session.Stop();
            session.StopEncoder();
        }

        /// <summary>
        /// Ends one recording: flushes and closes its encoder, leaving any other recording running.
        /// The last recording stops the camera as well, as stopping every recording does.
        /// </summary>
        public void StopRecording(Encoder encoder)
        {
            var encoders = Recording.Encoders.GetOrCreateValue(session);
            bool last;
            lock (encoders)
                last = encoders.Count == 1 && encoders.Contains(encoder);

            // With nothing left to feed, stop the camera first so no frame arrives mid-flush.
            if (last)
                session.Stop();
            session.StopEncoder(encoder);
        }

        /// <summary>
        /// Attaches an encoder to the running configuration without starting the camera: takes the
        /// frame size, stride, format and frame rate from the capture stream and starts the encoder.
        /// </summary>
        /// <exception cref="InvalidOperationException">The camera is not configured.</exception>
        public void StartEncoder(Encoder encoder, Output? output = null, Quality? quality = null)
        {
            var configuration = session.CameraConfiguration ?? throw new InvalidOperationException("Configure the camera before starting an encoder.");
            var stream = configuration.Capture;

            if (output is not null)
                encoder.Output = output;

            encoder.Width = (int)stream.Size!.Value.Width;
            encoder.Height = (int)stream.Size.Value.Height;
            encoder.Stride = stream.Stride!.Value;
            encoder.Format = stream.Format!.Value;
            encoder.ColourSpace = configuration.ColourSpace;

            // A nominal frame rate for encoders that must state one: the camera's fastest, at most 30 fps.
            if (encoder.FrameRate is null && session.CameraControls.TryGet(Controls.FrameDurationLimits) is { } limits)
            {
                var minimumDuration = Math.Max(limits.Min<long>(), 33333);
                encoder.FrameRate = 1_000_000.0 / minimumDuration;
            }
            encoder.FrameRate ??= 30;

            encoder.Start(quality);
            var encoders = Recording.Encoders.GetOrCreateValue(session);
            lock (encoders)
            {
                if (encoders.Count == 0)
                    session.FrameCompleted += session.EncodeFrame;
                encoders.Add(encoder);
            }
        }

        /// <summary>Stops <paramref name="encoder"/>, or every attached encoder when none is given.</summary>
        public void StopEncoder(Encoder? encoder = null)
        {
            var encoders = Recording.Encoders.GetOrCreateValue(session);
            Encoder[] stopping;
            lock (encoders)
            {
                stopping = encoder is null ? [.. encoders] : [.. encoders.Where(e => e == encoder)];
                foreach (var stopped in stopping)
                    encoders.Remove(stopped);
                if (encoders.Count == 0)
                    session.FrameCompleted -= session.EncodeFrame;
            }
            foreach (var stopped in stopping)
                stopped.Stop();
        }

        // Runs on libcamera's thread for every frame, so it only hands each encoder its own share of
        // the frame; the encoding happens on the encoder's thread, which returns the buffer when done.
        private void EncodeFrame(CapturedFrame request)
        {
            var encoders = Recording.Encoders.GetOrCreateValue(session);
            Encoder[] current;
            lock (encoders)
                current = [.. encoders];
            foreach (var encoder in current)
                encoder.Enqueue(request.Share());
        }
    }
}
