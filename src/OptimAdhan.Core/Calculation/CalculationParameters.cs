namespace OptimAdhan.Core.Calculation;

/// <summary>Determines Asr: Shafi (also Maliki, Hanbali) uses shadow length 1, Hanafi uses 2.</summary>
public enum Madhab
{
    Shafi,
    Hanafi,
}

/// <summary>Bounds Fajr and Isha at high latitudes where true twilight may not occur.</summary>
public enum HighLatitudeRule
{
    MiddleOfTheNight,
    SeventhOfTheNight,
    TwilightAngle,
}

/// <summary>How to resolve times inside the polar circle when the sun does not rise or set.</summary>
public enum PolarCircleResolution
{
    Unresolved,

    /// <summary>Use the nearest latitude where the sun rises and sets.</summary>
    AqrabBalad,

    /// <summary>Use the nearest date where the sun rises and sets.</summary>
    AqrabYaum,
}

/// <summary>Twilight definition used by the Moonsighting Committee method for Isha.</summary>
public enum Shafaq
{
    General,
    Ahmer,
    Abyad,
}

public enum Rounding
{
    Nearest,
    Up,
    None,
}

public enum CalculationMethod
{
    MuslimWorldLeague,
    Egyptian,
    Karachi,
    UmmAlQura,
    Dubai,
    MoonsightingCommittee,
    NorthAmerica,
    Kuwait,
    Qatar,
    Singapore,
    Tehran,
    Turkey,

    /// <summary>User-defined Fajr/Isha angles.</summary>
    Custom,
}

/// <summary>Minute offsets applied to each time.</summary>
public readonly record struct PrayerAdjustments(
    int Fajr = 0, int Sunrise = 0, int Dhuhr = 0, int Asr = 0, int Maghrib = 0, int Isha = 0)
{
    public static PrayerAdjustments None => default;
}

/// <summary>
/// Inputs to the prayer time calculation. Create from <see cref="CalculationMethods.For"/>
/// and customise with a <c>with</c> expression.
/// </summary>
public sealed record CalculationParameters
{
    public CalculationMethod Method { get; init; } = CalculationMethod.Custom;

    /// <summary>Sun angle below the horizon for Fajr, in degrees.</summary>
    public double FajrAngle { get; init; }

    /// <summary>Sun angle below the horizon for Isha, in degrees. Ignored when <see cref="IshaInterval"/> is set.</summary>
    public double IshaAngle { get; init; }

    /// <summary>Minutes after Maghrib for Isha; when greater than zero, replaces <see cref="IshaAngle"/>.</summary>
    public int IshaInterval { get; init; }

    /// <summary>Sun angle below the horizon for Maghrib (Tehran method only); 0 means sunset.</summary>
    public double MaghribAngle { get; init; }

    public Madhab Madhab { get; init; } = Madhab.Shafi;

    public HighLatitudeRule HighLatitudeRule { get; init; } = HighLatitudeRule.MiddleOfTheNight;

    public PolarCircleResolution PolarCircleResolution { get; init; } = PolarCircleResolution.Unresolved;

    public Rounding Rounding { get; init; } = Rounding.Nearest;

    public Shafaq Shafaq { get; init; } = Shafaq.General;

    /// <summary>User adjustments in minutes (for example, to match a local masjid).</summary>
    public PrayerAdjustments Adjustments { get; init; }

    /// <summary>Adjustments defined by the calculation method itself.</summary>
    public PrayerAdjustments MethodAdjustments { get; init; }

    internal (double Fajr, double Isha) NightPortions() => HighLatitudeRule switch
    {
        HighLatitudeRule.MiddleOfTheNight => (1.0 / 2, 1.0 / 2),
        HighLatitudeRule.SeventhOfTheNight => (1.0 / 7, 1.0 / 7),
        HighLatitudeRule.TwilightAngle => (FajrAngle / 60, IshaAngle / 60),
        _ => throw new InvalidOperationException($"Invalid high latitude rule: {HighLatitudeRule}"),
    };
}

/// <summary>Standard calculation methods, matching adhan-js 4.4.3.</summary>
public static class CalculationMethods
{
    public static IReadOnlyList<CalculationMethod> All { get; } = Enum.GetValues<CalculationMethod>();

    public static CalculationParameters For(CalculationMethod method) => method switch
    {
        CalculationMethod.MuslimWorldLeague => new()
        {
            Method = method, FajrAngle = 18, IshaAngle = 17, MethodAdjustments = new(Dhuhr: 1),
        },
        CalculationMethod.Egyptian => new()
        {
            Method = method, FajrAngle = 19.5, IshaAngle = 17.5, MethodAdjustments = new(Dhuhr: 1),
        },
        CalculationMethod.Karachi => new()
        {
            Method = method, FajrAngle = 18, IshaAngle = 18, MethodAdjustments = new(Dhuhr: 1),
        },
        CalculationMethod.UmmAlQura => new() { Method = method, FajrAngle = 18.5, IshaInterval = 90 },
        CalculationMethod.Dubai => new()
        {
            Method = method, FajrAngle = 18.2, IshaAngle = 18.2,
            MethodAdjustments = new(Sunrise: -3, Dhuhr: 3, Asr: 3, Maghrib: 3),
        },
        CalculationMethod.MoonsightingCommittee => new()
        {
            Method = method, FajrAngle = 18, IshaAngle = 18, MethodAdjustments = new(Dhuhr: 5, Maghrib: 3),
        },
        CalculationMethod.NorthAmerica => new()
        {
            Method = method, FajrAngle = 15, IshaAngle = 15, MethodAdjustments = new(Dhuhr: 1),
        },
        CalculationMethod.Kuwait => new() { Method = method, FajrAngle = 18, IshaAngle = 17.5 },
        CalculationMethod.Qatar => new() { Method = method, FajrAngle = 18, IshaInterval = 90 },
        CalculationMethod.Singapore => new()
        {
            Method = method, FajrAngle = 20, IshaAngle = 18, MethodAdjustments = new(Dhuhr: 1),
            Rounding = Rounding.Up,
        },
        CalculationMethod.Tehran => new() { Method = method, FajrAngle = 17.7, IshaAngle = 14, MaghribAngle = 4.5 },
        CalculationMethod.Turkey => new()
        {
            Method = method, FajrAngle = 18, IshaAngle = 17,
            MethodAdjustments = new(Sunrise: -7, Dhuhr: 5, Asr: 4, Maghrib: 7),
        },
        CalculationMethod.Custom => new() { Method = method, FajrAngle = 18, IshaAngle = 17 },
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, null),
    };

    public static string DisplayName(CalculationMethod method) => method switch
    {
        CalculationMethod.MuslimWorldLeague => "Muslim World League",
        CalculationMethod.Egyptian => "Egyptian General Authority of Survey",
        CalculationMethod.Karachi => "University of Islamic Sciences, Karachi",
        CalculationMethod.UmmAlQura => "Umm al-Qura University, Makkah",
        CalculationMethod.Dubai => "Dubai",
        CalculationMethod.MoonsightingCommittee => "Moonsighting Committee Worldwide",
        CalculationMethod.NorthAmerica => "Islamic Society of North America (ISNA)",
        CalculationMethod.Kuwait => "Kuwait",
        CalculationMethod.Qatar => "Qatar",
        CalculationMethod.Singapore => "Majlis Ugama Islam Singapura",
        CalculationMethod.Tehran => "Institute of Geophysics, University of Tehran",
        CalculationMethod.Turkey => "Diyanet İşleri Başkanlığı, Turkey",
        CalculationMethod.Custom => "Custom angles",
        _ => method.ToString(),
    };

    /// <summary>
    /// Suggested method by ISO country code, identical to the web app's
    /// calculation-method-by-region.ts. Users can always override it.
    /// </summary>
    public static CalculationMethod ForCountry(string? countryCode) => countryCode?.ToUpperInvariant() switch
    {
        "US" or "CA" or "MX" => CalculationMethod.NorthAmerica,
        "SA" => CalculationMethod.UmmAlQura,
        "AE" => CalculationMethod.Dubai,
        "KW" => CalculationMethod.Kuwait,
        "QA" => CalculationMethod.Qatar,
        "IR" => CalculationMethod.Tehran,
        "PK" or "IN" or "BD" => CalculationMethod.Karachi,
        "SG" or "MY" or "ID" => CalculationMethod.Singapore,
        "TR" => CalculationMethod.Turkey,
        "EG" or "LY" or "TN" or "DZ" or "MA" or "SD" => CalculationMethod.Egyptian,
        _ => CalculationMethod.MuslimWorldLeague,
    };
}
