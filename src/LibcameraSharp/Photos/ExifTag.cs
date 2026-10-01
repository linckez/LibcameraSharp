namespace LibcameraSharp;

/// <summary>The EXIF tags the SDK writes. Each knows its directory; the value's type is the one the <c>Set</c> overload called takes.</summary>
internal enum ExifTag : ushort
{
    /// <summary>Camera manufacturer (IFD0, ASCII).</summary>
    Make = 271,
    /// <summary>Camera model (IFD0, ASCII).</summary>
    Model = 272,
    /// <summary>Software that wrote the file (IFD0, ASCII).</summary>
    Software = 305,
    /// <summary>File change time, <c>YYYY:MM:DD HH:MM:SS</c> (IFD0, ASCII).</summary>
    DateTime = 306,
    /// <summary>Person who created the image (IFD0, ASCII).</summary>
    Artist = 315,
    /// <summary>Copyright notice (IFD0, ASCII).</summary>
    Copyright = 33432,
    /// <summary>Free-text description (IFD0, ASCII).</summary>
    ImageDescription = 270,
    /// <summary>Exposure time in seconds (EXIF, rational).</summary>
    ExposureTime = 33434,
    /// <summary>ISO speed (EXIF, short).</summary>
    IsoSpeedRatings = 34855,
    /// <summary>When the picture was taken (EXIF, ASCII).</summary>
    DateTimeOriginal = 36867,
    /// <summary>When the picture was stored (EXIF, ASCII).</summary>
    DateTimeDigitized = 36868,
    /// <summary>Distance to the subject in metres (EXIF, rational).</summary>
    SubjectDistance = 37382,
    /// <summary>User comment (EXIF, undefined bytes behind an 8-byte character code).</summary>
    UserComment = 37510,
    /// <summary>The GPS directory's version, 2.2.0.0 (GPS, 4 bytes).</summary>
    GpsVersionId = 0,
    /// <summary><c>N</c> or <c>S</c> (GPS, ASCII).</summary>
    GpsLatitudeRef = 1,
    /// <summary>Degrees, minutes and seconds (GPS, 3 rationals).</summary>
    GpsLatitude = 2,
    /// <summary><c>E</c> or <c>W</c> (GPS, ASCII).</summary>
    GpsLongitudeRef = 3,
    /// <summary>Degrees, minutes and seconds (GPS, 3 rationals).</summary>
    GpsLongitude = 4,
    /// <summary>0 above sea level, 1 below (GPS, byte).</summary>
    GpsAltitudeRef = 5,
    /// <summary>Metres from sea level (GPS, rational).</summary>
    GpsAltitude = 6,
    /// <summary>The geodetic datum, <c>WGS-84</c> (GPS, ASCII).</summary>
    GpsMapDatum = 18,
}
