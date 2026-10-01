namespace LibcameraSharp;

/// <summary>
/// Writes a raw frame as an Adobe DNG that Lightroom, darktable, RawTherapee and dcraw open: the
/// unpacked 16-bit samples, the sensor's CFA pattern and black/white levels, and the colour
/// calibration the ISP reported for the frame (white balance gains and colour matrix), so the
/// developed image matches the camera's own JPEG.
/// </summary>
/// <remarks>
/// Layout: a greyscale thumbnail in IFD0, the raw image in a SubIFD, and an EXIF IFD, written by
/// libtiff. Black levels are reordered from libcamera's R/Gr/Gb/B into CFA order; ColorMatrix1 is
/// (RGB→XYZ · CCM · WB)⁻¹ under D65.
/// </remarks>
internal static class DngWriter
{
    // Tag numbers, as tiff.h defines them; the ones a JPEG's EXIF also carries come from ExifTag.
    private const ushort SubfileType = 254, ImageWidth = 256, ImageLength = 257, BitsPerSample = 258, Compression = 259, Photometric = 262,
        Orientation = 274, SamplesPerPixel = 277, PlanarConfig = 284, SubIfds = 330,
        CfaRepeatPatternDim = 33421, CfaPattern = 33422, ExifIfd = 34665,
        DngVersion = 50706, DngBackwardVersion = 50707, UniqueCameraModel = 50708, BlackLevelRepeatDim = 50713,
        BlackLevel = 50714, WhiteLevel = 50717, ColorMatrix1 = 50721, CameraCalibration1 = 50723, CameraCalibration2 = 50724,
        AsShotNeutral = 50728, CalibrationIlluminant1 = 50778;
    private const ushort Make = (ushort)ExifTag.Make, Model = (ushort)ExifTag.Model, Software = (ushort)ExifTag.Software,
        ExposureTime = (ushort)ExifTag.ExposureTime, IsoSpeedRatings = (ushort)ExifTag.IsoSpeedRatings,
        DateTimeOriginal = (ushort)ExifTag.DateTimeOriginal, SubjectDistance = (ushort)ExifTag.SubjectDistance;

    private const uint CompressionNone = 1, PhotometricRgb = 2, PhotometricCfa = 32803, PhotometricLinearRaw = 34892, OrientationTopLeft = 1,
        PlanarContiguous = 1, IlluminantD65 = 21;

    // SubfileType: the thumbnail is a reduced-resolution image, the raw one the full image (tiff.h FILETYPE_REDUCEDIMAGE).
    private const uint ReducedImage = 1, FullImage = 0;

    // DNG 1.4, readable by readers of DNG 1.0 onwards.
    private static readonly byte[] Version = [1, 4, 0, 0], BackwardVersion = [1, 0, 0, 0];

    // What the file says when the frame's metadata doesn't: a 10 ms exposure at unity gain.
    private const int FallbackExposureMicroseconds = 10_000;
    private const float UnityGain = 1f;

    // The thumbnail is 1/16 of the image each way. Four samples summed add two bits, so shifting by 14 then down by
    // the sample depth puts the sum on a 16-bit scale, whose square root fits a byte.
    private const int ThumbnailShift = 4, SummedTo16Bits = 14;

    // After the directories are written, libtiff leaves the last one also chained as a third top-level directory.
    private const int StrayDirectory = 2;

    /// <summary>
    /// Writes the raw stream buffer <paramref name="raw"/> described by <paramref name="config"/> as a DNG
    /// file at <paramref name="path"/>. <paramref name="metadata"/> supplies black levels, gains and the
    /// colour matrix; missing ones get defaults. <paramref name="cameraModel"/> is the sensor name.
    /// </summary>
    /// <exception cref="NotSupportedException">The stream's format isn't a raw format libcamera knows, or its packing can't be unpacked.</exception>
    /// <exception cref="InvalidOperationException">libtiff isn't installed, or couldn't write the file.</exception>
    public static void Save(ReadOnlySpan<byte> raw, StreamDescription config, Metadata metadata, string cameraModel, string path)
    {
        var format = BayerFormat.FromPixelFormat(config.Format ?? PixelFormat.Invalid)
                     ?? throw new NotSupportedException($"{config.Format} is not a raw format libcamera knows.");
        var size = config.Size ?? throw new ArgumentException("The stream configuration has no size.", nameof(config));
        var (width, height) = ((int)size.Width, (int)size.Height);
        var samples = RawUnpacking.Unpack(raw, format, size, config.Stride ?? throw new ArgumentException("The stream configuration has no stride.", nameof(config)));
        var bits = RawUnpacking.UnpackedBitDepth(format);
        var white = (1u << bits) - 1;
        var mono = format.IsMono;

        // Black levels: libcamera reports R, Gr, Gb, B on a 16-bit scale; the DNG wants CFA-position order at the sample depth.
        var blackLevels = BlackLevels(metadata, format, bits);

        // Colour: ColorMatrix1 maps XYZ to camera RGB, so invert (sRGB→XYZ · CCM · white-balance gains).
        var gains = metadata.TryGet(Controls.ColourGains, out var cg) && cg.Length == Controls.ColourGains.FixedLength ? cg : [UnityGain, UnityGain];
        float[] neutral = [1f / gains[0], 1f, 1f / gains[1]];
        var ccm = metadata.TryGet(Controls.ColourCorrectionMatrix, out var m) && m.Length == Controls.ColourCorrectionMatrix.FixedLength ? m.Select(v => (double)v).ToArray() : DefaultCcm;
        var cameraToXyz = Matrix3.Invert(Matrix3.Multiply(Matrix3.Multiply(Rgb2Xyz, ccm), Matrix3.Diagonal(gains[0], 1, gains[1])));

        var exposureUs = metadata.TryGet(Controls.ExposureTime, out var exp) ? exp : FallbackExposureMicroseconds;
        var iso = ExifSegment.Iso((metadata.TryGet(Controls.AnalogueGain, out var ag) ? ag : UnityGain) * (metadata.TryGet(Controls.DigitalGain, out var dg) ? dg : UnityGain));
        var timestamp = ExifSegment.DateTimeText(DateTime.Now);
        var thumb = Thumbnail(samples, width, height, bits, out var thumbWidth, out var thumbHeight);

        var tif = Libtiff.Create(path);
        try
        {
            // IFD0: a 1/16-scale greyscale thumbnail, first so software that reads only one IFD shows something.
            Libtiff.Set(tif, SubfileType, ReducedImage);
            Libtiff.Set(tif, ImageWidth, (uint)thumbWidth);
            Libtiff.Set(tif, ImageLength, (uint)thumbHeight);
            Libtiff.Set(tif, BitsPerSample, 8);
            Libtiff.Set(tif, Compression, CompressionNone);
            Libtiff.Set(tif, Photometric, PhotometricRgb);
            if (ExifSegment.Maker is { } maker)
                Libtiff.Set(tif, Make, maker);
            Libtiff.Set(tif, Model, cameraModel);
            Libtiff.SetFixed<byte>(tif, DngVersion, Version);
            Libtiff.SetFixed<byte>(tif, DngBackwardVersion, BackwardVersion);
            Libtiff.Set(tif, UniqueCameraModel, ExifSegment.Maker is { } make ? $"{make} {cameraModel}" : cameraModel);
            Libtiff.Set(tif, Orientation, OrientationTopLeft);
            Libtiff.Set(tif, SamplesPerPixel, 3);
            Libtiff.Set(tif, PlanarConfig, PlanarContiguous);
            Libtiff.Set(tif, Software, ExifSegment.Software);
            if (!mono)
            {
                Libtiff.Set<float>(tif, ColorMatrix1, [.. cameraToXyz.Select(v => (float)v)]);
                Libtiff.Set<float>(tif, CameraCalibration1, Identity3);
                Libtiff.Set<float>(tif, CameraCalibration2, Identity3);
                Libtiff.Set<float>(tif, AsShotNeutral, neutral);
                Libtiff.Set(tif, CalibrationIlluminant1, IlluminantD65);
            }

            // Placeholders for the raw image's and the EXIF directory's offsets, filled in at the end.
            Libtiff.Set<ulong>(tif, SubIfds, [0]);
            Libtiff.SetOffset(tif, ExifIfd, 0);
            for (var y = 0; y < thumbHeight; y++)
                Libtiff.WriteRow<byte>(tif, thumb.AsSpan(y * thumbWidth * 3, thumbWidth * 3), (uint)y);
            Libtiff.WriteDirectory(tif);

            // The raw image itself, which libtiff writes as IFD0's sub-directory.
            Libtiff.RegisterCfaFields(tif);
            Libtiff.Set(tif, SubfileType, FullImage);
            Libtiff.Set(tif, ImageWidth, (uint)width);
            Libtiff.Set(tif, ImageLength, (uint)height);
            Libtiff.Set(tif, BitsPerSample, 16);
            Libtiff.Set(tif, Photometric, mono ? PhotometricLinearRaw : PhotometricCfa);
            Libtiff.Set(tif, SamplesPerPixel, 1);
            Libtiff.Set(tif, PlanarConfig, PlanarContiguous);
            Libtiff.Set<uint>(tif, WhiteLevel, [white]);
            if (mono)
            {
                Libtiff.SetFixed<ushort>(tif, BlackLevelRepeatDim, [1, 1]);
                Libtiff.Set<float>(tif, BlackLevel, [(float)blackLevels[0]]);
            }
            else
            {
                Libtiff.SetFixed<ushort>(tif, CfaRepeatPatternDim, [2, 2]);
                Libtiff.Set<byte>(tif, CfaPattern, CfaPatternOf(format.Order));
                Libtiff.SetFixed<ushort>(tif, BlackLevelRepeatDim, [2, 2]);
                Libtiff.Set<float>(tif, BlackLevel, [.. blackLevels.Select(v => (float)v)]);
            }
            for (var y = 0; y < height; y++)
                Libtiff.WriteRow<ushort>(tif, samples.AsSpan(y * width, width), (uint)y);
            var rawOffset = Libtiff.CheckpointDirectory(tif);
            Libtiff.WriteDirectory(tif);

            // EXIF directory: exposure time, ISO and when the frame was taken.
            Libtiff.CreateExifDirectory(tif);
            Libtiff.Set(tif, DateTimeOriginal, timestamp);
            Libtiff.Set<ushort>(tif, IsoSpeedRatings, [iso]);
            Libtiff.Set(tif, ExposureTime, exposureUs / 1e6);
            if (metadata.TryGet(Controls.LensPosition, out var lensPosition) && lensPosition > 0)
                Libtiff.Set(tif, SubjectDistance, 1.0 / lensPosition);
            var exifOffset = Libtiff.CheckpointDirectory(tif);
            Libtiff.WriteDirectory(tif);

            // Back to IFD0 to fill in the two offsets. libtiff then leaves the last directory written
            // also chained as a second top-level directory, which some readers complain about; unlink it.
            Libtiff.SetDirectory(tif, 0);
            Libtiff.Set<ulong>(tif, SubIfds, [rawOffset]);
            Libtiff.SetOffset(tif, ExifIfd, exifOffset);
            Libtiff.WriteDirectory(tif);
            Libtiff.UnlinkDirectory(tif, StrayDirectory);
        }
        finally
        {
            Libtiff.Close(tif);
        }
    }

    // Used when the metadata lacks them: black at 4096/65536 of full scale, and a plausible CCM.
    private const int FallbackBlackLevel = 4096, BlackLevelScale = 65536;
    private static readonly double[] DefaultCcm = [1.90255, -0.77478, -0.12777, -0.31338, 1.88197, -0.56858, -0.06001, -0.61785, 1.67786];

    // sRGB (D65) to XYZ, from http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html.
    private static readonly double[] Rgb2Xyz = [0.4124564, 0.3575761, 0.1804375, 0.2126729, 0.7151522, 0.0721750, 0.0193339, 0.1191920, 0.9503041];

    private static readonly float[] Identity3 = [1, 0, 0, 0, 1, 0, 0, 0, 1];

    private static double[] BlackLevels(Metadata metadata, BayerFormat format, int bits)
    {
        var scale = (1 << bits) / (double)BlackLevelScale;
        if (!metadata.TryGet(Controls.SensorBlackLevels, out var reported) || reported.Length < Controls.SensorBlackLevels.FixedLength)
        {
            var fallback = FallbackBlackLevel * scale;
            return [fallback, fallback, fallback, fallback];
        }
        if (format.IsMono)
            return [reported[0] * scale, 0, 0, 0];

        // Each CFA position takes the level of its colour: R → slot 0, B → slot 3, G → Gr (1) when its row neighbour is R, else Gb (2).
        var colours = CfaPatternOf(format.Order);
        var levels = new double[4];
        for (var position = 0; position < 4; position++)
        {
            var colour = colours[position];
            var slot = colour == 0 ? 0 : colour == 2 ? 3 : 1 + (colours[position ^ 1] == 0 ? 0 : 1);
            levels[position] = reported[slot] * scale;
        }
        return levels;
    }

    // TIFF CFA colour codes 0 = red, 1 = green, 2 = blue, in row-major 2×2 order.
    private static byte[] CfaPatternOf(BayerOrder order) => order switch
    {
        BayerOrder.RGGB => [0, 1, 1, 2],
        BayerOrder.GRBG => [1, 0, 2, 1],
        BayerOrder.BGGR => [2, 1, 1, 0],
        BayerOrder.GBRG => [1, 2, 0, 1],
        _ => throw new NotSupportedException($"No CFA pattern for {order}."),
    };

    // A 1/16-scale grey preview: sum of the top-left 2×2 block of each 16×16 tile, scaled to 8 bits, square-rooted as a crude gamma.
    private static byte[] Thumbnail(ushort[] samples, int width, int height, int bits, out int thumbWidth, out int thumbHeight)
    {
        thumbWidth = width >> ThumbnailShift;
        thumbHeight = height >> ThumbnailShift;
        var thumb = new byte[thumbWidth * thumbHeight * 3];
        for (var y = 0; y < thumbHeight; y++)
        {
            for (var x = 0; x < thumbWidth; x++)
            {
                var offset = ((y * width) + x) << ThumbnailShift;
                uint grey = (uint)(samples[offset] + samples[offset + 1] + samples[offset + width] + samples[offset + width + 1]);
                grey = (grey << SummedTo16Bits) >> bits;
                var value = (byte)Math.Min(byte.MaxValue, Math.Sqrt(grey));
                var i = 3 * (y * thumbWidth + x);
                thumb[i] = thumb[i + 1] = thumb[i + 2] = value;
            }
        }
        return thumb;
    }

    // 3×3 row-major arithmetic for the colour matrix.
    private static class Matrix3
    {
        public static double[] Multiply(double[] a, double[] b)
        {
            var r = new double[9];
            for (var i = 0; i < 3; i++)
                for (var j = 0; j < 3; j++)
                    r[3 * i + j] = a[3 * i] * b[j] + a[3 * i + 1] * b[3 + j] + a[3 * i + 2] * b[6 + j];
            return r;
        }

        public static double[] Diagonal(double a, double b, double c) => [a, 0, 0, 0, b, 0, 0, 0, c];

        public static double[] Invert(double[] m)
        {
            var det = m[0] * (m[4] * m[8] - m[5] * m[7]) - m[1] * (m[3] * m[8] - m[5] * m[6]) + m[2] * (m[3] * m[7] - m[4] * m[6]);
            return
            [
                (m[4] * m[8] - m[5] * m[7]) / det, (m[2] * m[7] - m[1] * m[8]) / det, (m[1] * m[5] - m[2] * m[4]) / det,
                (m[5] * m[6] - m[3] * m[8]) / det, (m[0] * m[8] - m[2] * m[6]) / det, (m[2] * m[3] - m[0] * m[5]) / det,
                (m[3] * m[7] - m[4] * m[6]) / det, (m[1] * m[6] - m[0] * m[7]) / det, (m[0] * m[4] - m[1] * m[3]) / det,
            ];
        }
    }
}
