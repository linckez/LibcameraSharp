namespace LibcameraSharp;

/// <summary>Where a photo was taken, in decimal degrees on the WGS-84 datum GPS uses, with the height if known.</summary>
public sealed record GpsLocation
{
    /// <summary>A location: north and east are positive.</summary>
    /// <param name="latitude">Degrees north of the equator, -90 to 90.</param>
    /// <param name="longitude">Degrees east of Greenwich, -180 to 180.</param>
    /// <param name="altitude">Metres above sea level (negative below it), when known.</param>
    /// <exception cref="ArgumentOutOfRangeException">The latitude or longitude is out of range, or not a number.</exception>
    public GpsLocation(double latitude, double longitude, double? altitude = null)
    {
        if (!(latitude is >= -90 and <= 90))
            throw new ArgumentOutOfRangeException(nameof(latitude), latitude, "A latitude runs from -90 to 90 degrees.");
        if (!(longitude is >= -180 and <= 180))
            throw new ArgumentOutOfRangeException(nameof(longitude), longitude, "A longitude runs from -180 to 180 degrees.");
        (Latitude, Longitude, Altitude) = (latitude, longitude, altitude);
    }

    /// <summary>Degrees north of the equator; negative is south.</summary>
    public double Latitude { get; }

    /// <summary>Degrees east of Greenwich; negative is west.</summary>
    public double Longitude { get; }

    /// <summary>Metres above sea level, negative below it, or null when not known.</summary>
    public double? Altitude { get; }

    // The GPS directory's version, and the datum every GPS fix is on.
    private static readonly byte[] Version = [2, 2, 0, 0];
    private const string Datum = "WGS-84";

    // Seconds are kept to a ten-millionth, which still fits a 32-bit numerator (60e7 < 2³²); altitude to a centimetre.
    private const uint SecondsDenominator = 10_000_000, AltitudeDenominator = 100;

    // Writes the GPS directory's tags.
    internal void WriteTo(ExifTagValues tags)
    {
        tags.Set(ExifTag.GpsVersionId, Version);
        tags.Set(ExifTag.GpsLatitudeRef, Latitude >= 0 ? "N" : "S");
        tags.Set(ExifTag.GpsLatitude, DegreesMinutesSeconds(Latitude));
        tags.Set(ExifTag.GpsLongitudeRef, Longitude >= 0 ? "E" : "W");
        tags.Set(ExifTag.GpsLongitude, DegreesMinutesSeconds(Longitude));
        if (Altitude is { } altitude)
        {
            tags.Set(ExifTag.GpsAltitudeRef, [(byte)(altitude >= 0 ? 0 : 1)]);
            tags.Set(ExifTag.GpsAltitude, (uint)(Math.Abs(altitude) * AltitudeDenominator), AltitudeDenominator);
        }
        tags.Set(ExifTag.GpsMapDatum, Datum);
    }

    // Whole degrees, whole minutes, and the seconds left over, of the angle's size (its sign is the N/S or E/W ref).
    internal static (uint Numerator, uint Denominator)[] DegreesMinutesSeconds(double angle)
    {
        var value = Math.Abs(angle);
        var degrees = (long)value;
        var minutes = (long)((value - degrees) * 60);
        var seconds = Math.Round((value - degrees - minutes / 60.0) * 3600 * SecondsDenominator);
        return [((uint)degrees, 1), ((uint)minutes, 1), ((uint)seconds, SecondsDenominator)];
    }
}
