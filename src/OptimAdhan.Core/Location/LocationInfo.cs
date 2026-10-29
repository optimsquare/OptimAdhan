using System.Text.Json.Serialization;
using GeoTimeZone;
using OptimAdhan.Core.Calculation;

namespace OptimAdhan.Core.Location;

public enum LocationSource
{
    /// <summary>Picked from search results.</summary>
    Search,

    /// <summary>Detected by Windows location services.</summary>
    Detected,

    /// <summary>Coordinates typed in by the user.</summary>
    Manual,
}

/// <summary>
/// A place prayer times are calculated for. <see cref="TimeZoneId"/> is the IANA
/// zone of the place itself (not of the PC), so times and day boundaries are
/// correct even when the selected city is in another time zone.
/// </summary>
public sealed record LocationInfo
{
    public required string DisplayName { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    /// <summary>IANA time zone ID, such as "America/New_York".</summary>
    public required string TimeZoneId { get; init; }

    /// <summary>ISO 3166-1 alpha-2 code, when known.</summary>
    public string? CountryCode { get; init; }

    public LocationSource Source { get; init; } = LocationSource.Search;

    [JsonIgnore]
    public Coordinates Coordinates => new(Latitude, Longitude);

    /// <summary>The place name, which is also what screen readers announce in lists.</summary>
    public override string ToString() => DisplayName;

    /// <summary>Creates a location, resolving the time zone from its coordinates offline.</summary>
    public static LocationInfo Create(
        string displayName, double latitude, double longitude, string? countryCode, LocationSource source) =>
        new()
        {
            DisplayName = displayName,
            Latitude = latitude,
            Longitude = longitude,
            TimeZoneId = TimeZones.IanaIdFor(latitude, longitude),
            CountryCode = countryCode?.ToUpperInvariant(),
            Source = source,
        };
}

public static class TimeZones
{
    /// <summary>
    /// IANA time zone at the coordinates, from the bundled boundary data in
    /// GeoTimeZone (no network). Works over oceans too (returns Etc/GMT±N).
    /// </summary>
    public static string IanaIdFor(double latitude, double longitude) =>
        TimeZoneLookup.GetTimeZone(latitude, longitude).Result;

    /// <summary>
    /// Resolves an IANA ID to a <see cref="TimeZoneInfo"/>. .NET applies the
    /// operating system's daylight saving rules, which Windows Update keeps current.
    /// </summary>
    public static TimeZoneInfo Find(string ianaId)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(ianaId, out var zone))
        {
            return zone;
        }

        if (TimeZoneInfo.TryConvertIanaIdToWindowsId(ianaId, out var windowsId)
            && TimeZoneInfo.TryFindSystemTimeZoneById(windowsId, out zone))
        {
            return zone;
        }

        throw new TimeZoneNotFoundException($"Time zone '{ianaId}' is not available on this system.");
    }
}
