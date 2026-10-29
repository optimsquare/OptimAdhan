namespace OptimAdhan.Core.Calculation;

public enum Prayer
{
    Fajr,
    Sunrise,
    Dhuhr,
    Asr,
    Maghrib,
    Isha,
}

/// <summary>
/// Prayer times for one calendar date at one location, as UTC instants.
/// A null time means it cannot be determined for this date and location
/// (for example, inside the polar circle with no resolution rule). Callers must
/// show that explicitly; the engine never substitutes estimated values.
/// </summary>
/// <remarks>Port of adhan-js 4.4.3 PrayerTimes.js; parity is verified by fixture tests.</remarks>
public sealed class PrayerTimes
{
    private PrayerTimes(DateOnly date, Coordinates coordinates, CalculationParameters parameters)
    {
        Date = date;
        Coordinates = coordinates;
        Parameters = parameters;
    }

    /// <summary>The calendar date in the location's own time zone.</summary>
    public DateOnly Date { get; }

    public Coordinates Coordinates { get; }

    public CalculationParameters Parameters { get; }

    public DateTime? Fajr { get; private init; }

    public DateTime? Sunrise { get; private init; }

    public DateTime? Dhuhr { get; private init; }

    public DateTime? Asr { get; private init; }

    public DateTime? Sunset { get; private init; }

    public DateTime? Maghrib { get; private init; }

    public DateTime? Isha { get; private init; }

    public bool IsComplete =>
        Fajr is not null && Sunrise is not null && Dhuhr is not null && Asr is not null
        && Sunset is not null && Maghrib is not null && Isha is not null;

    public DateTime? TimeFor(Prayer prayer) => prayer switch
    {
        Prayer.Fajr => Fajr,
        Prayer.Sunrise => Sunrise,
        Prayer.Dhuhr => Dhuhr,
        Prayer.Asr => Asr,
        Prayer.Maghrib => Maghrib,
        Prayer.Isha => Isha,
        _ => throw new ArgumentOutOfRangeException(nameof(prayer), prayer, null),
    };

    public static PrayerTimes Calculate(Coordinates coordinates, DateOnly date, CalculationParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ValidateCoordinates(coordinates);

        var solarTime = new SolarTime(date, coordinates);
        var tomorrow = date.AddDays(1);
        var tomorrowSolarTime = new SolarTime(tomorrow, coordinates);

        var dhuhrTime = SolarTime.ToUtc(solarTime.Transit, date);
        var sunriseTime = SolarTime.ToUtc(solarTime.Sunrise, date);
        var sunsetTime = SolarTime.ToUtc(solarTime.Sunset, date);

        if ((sunriseTime is null || sunsetTime is null || double.IsNaN(tomorrowSolarTime.Sunrise))
            && parameters.PolarCircleResolution != PolarCircleResolution.Unresolved)
        {
            (solarTime, tomorrowSolarTime) = PolarCircle.Resolve(parameters.PolarCircleResolution, date, coordinates);
            dhuhrTime = SolarTime.ToUtc(solarTime.Transit, date);
            sunriseTime = SolarTime.ToUtc(solarTime.Sunrise, date);
            sunsetTime = SolarTime.ToUtc(solarTime.Sunset, date);
        }

        var asrTime = SolarTime.ToUtc(solarTime.Afternoon(ShadowLength(parameters.Madhab)), date);
        var tomorrowSunrise = SolarTime.ToUtc(tomorrowSolarTime.Sunrise, tomorrow);
        double? night = tomorrowSunrise is { } ts && sunsetTime is { } ss ? (ts - ss).TotalSeconds : null;

        var isMoonsighting = parameters.Method == CalculationMethod.MoonsightingCommittee;

        var fajrTime = SolarTime.ToUtc(solarTime.HourAngle(-1 * parameters.FajrAngle, afterTransit: false), date);

        // Special case for the Moonsighting Committee above latitude 55.
        if (isMoonsighting && coordinates.Latitude >= 55)
        {
            fajrTime = AddSeconds(sunriseTime, -(night / 7));
        }

        DateTime? safeFajr = isMoonsighting
            ? sunriseTime is { } sr
                ? Astronomical.SeasonAdjustedMorningTwilight(coordinates.Latitude, date.DayOfYear, date.Year, sr)
                : null
            : AddSeconds(sunriseTime, -(parameters.NightPortions().Fajr * night));

        if (fajrTime is null || safeFajr > fajrTime)
        {
            fajrTime = safeFajr;
        }

        DateTime? ishaTime;
        if (parameters.IshaInterval > 0)
        {
            ishaTime = AddMinutes(sunsetTime, parameters.IshaInterval);
        }
        else
        {
            ishaTime = SolarTime.ToUtc(solarTime.HourAngle(-1 * parameters.IshaAngle, afterTransit: true), date);

            // Special case for the Moonsighting Committee above latitude 55.
            if (isMoonsighting && coordinates.Latitude >= 55)
            {
                ishaTime = AddSeconds(sunsetTime, night / 7);
            }

            DateTime? safeIsha = isMoonsighting
                ? sunsetTime is { } st
                    ? Astronomical.SeasonAdjustedEveningTwilight(
                        coordinates.Latitude, date.DayOfYear, date.Year, st, parameters.Shafaq)
                    : null
                : AddSeconds(sunsetTime, parameters.NightPortions().Isha * night);

            if (ishaTime is null || safeIsha < ishaTime)
            {
                ishaTime = safeIsha;
            }
        }

        var maghribTime = sunsetTime;
        if (parameters.MaghribAngle != 0)
        {
            var angleBasedMaghrib = SolarTime.ToUtc(
                solarTime.HourAngle(-1 * parameters.MaghribAngle, afterTransit: true), date);
            if (sunsetTime < angleBasedMaghrib && ishaTime > angleBasedMaghrib)
            {
                maghribTime = angleBasedMaghrib;
            }
        }

        var user = parameters.Adjustments;
        var method = parameters.MethodAdjustments;
        var rounding = parameters.Rounding;

        return new PrayerTimes(date, coordinates, parameters)
        {
            Fajr = RoundedMinute(AddMinutes(fajrTime, user.Fajr + method.Fajr), rounding),
            Sunrise = RoundedMinute(AddMinutes(sunriseTime, user.Sunrise + method.Sunrise), rounding),
            Dhuhr = RoundedMinute(AddMinutes(dhuhrTime, user.Dhuhr + method.Dhuhr), rounding),
            Asr = RoundedMinute(AddMinutes(asrTime, user.Asr + method.Asr), rounding),
            Sunset = RoundedMinute(sunsetTime, rounding),
            Maghrib = RoundedMinute(AddMinutes(maghribTime, user.Maghrib + method.Maghrib), rounding),
            Isha = RoundedMinute(AddMinutes(ishaTime, user.Isha + method.Isha), rounding),
        };
    }

    private static void ValidateCoordinates(Coordinates coordinates)
    {
        if (!double.IsFinite(coordinates.Latitude) || coordinates.Latitude is < -90 or > 90)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinates), coordinates.Latitude, "Latitude must be between -90 and 90.");
        }

        if (!double.IsFinite(coordinates.Longitude) || coordinates.Longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(coordinates), coordinates.Longitude, "Longitude must be between -180 and 180.");
        }
    }

    private static double ShadowLength(Madhab madhab) => madhab == Madhab.Hanafi ? 2 : 1;

    private static DateTime? AddSeconds(DateTime? date, double? seconds) =>
        date is { } d && seconds is { } s && double.IsFinite(s) ? JsDate.AddSeconds(d, s) : null;

    private static DateTime? AddMinutes(DateTime? date, double minutes) => AddSeconds(date, minutes * 60);

    private static DateTime? RoundedMinute(DateTime? date, Rounding rounding)
    {
        if (date is not { } d)
        {
            return null;
        }

        var seconds = d.Second;
        var offset = rounding switch
        {
            Rounding.Up => 60 - seconds,
            Rounding.None => 0,
            _ => seconds >= 30 ? 60 - seconds : -seconds,
        };

        return d.AddSeconds(offset);
    }
}

// Port of adhan-js PolarCircleResolution.js.
internal static class PolarCircle
{
    private const double LatitudeVariationStep = 0.5;
    private const double UnsafeLatitude = 65;

    public static (SolarTime Today, SolarTime Tomorrow) Resolve(
        PolarCircleResolution resolution, DateOnly date, Coordinates coordinates)
    {
        var resolved = resolution switch
        {
            PolarCircleResolution.AqrabYaum => AqrabYaum(coordinates, date),
            PolarCircleResolution.AqrabBalad => AqrabBalad(
                coordinates, date, coordinates.Latitude - Math.Sign(coordinates.Latitude) * LatitudeVariationStep),
            _ => null,
        };

        return resolved ?? (new SolarTime(date, coordinates), new SolarTime(date.AddDays(1), coordinates));
    }

    private static (SolarTime, SolarTime)? AqrabYaum(Coordinates coordinates, DateOnly date)
    {
        // Iterative form of the recursive search in adhan-js, alternating
        // forward and backward one day at a time: +1, -1, +2, -2, ...
        var daysAdded = 1;
        var direction = 1;
        while (daysAdded <= (int)Math.Ceiling(365 / 2.0))
        {
            var testDate = date.AddDays(direction * daysAdded);
            var solarTime = new SolarTime(testDate, coordinates);
            var tomorrowSolarTime = new SolarTime(testDate.AddDays(1), coordinates);
            if (solarTime.IsValid && tomorrowSolarTime.IsValid)
            {
                return (solarTime, tomorrowSolarTime);
            }

            daysAdded += direction > 0 ? 0 : 1;
            direction = -direction;
        }

        return null;
    }

    private static (SolarTime, SolarTime)? AqrabBalad(Coordinates coordinates, DateOnly date, double latitude)
    {
        while (true)
        {
            var adjusted = coordinates with { Latitude = latitude };
            var solarTime = new SolarTime(date, adjusted);
            var tomorrowSolarTime = new SolarTime(date.AddDays(1), adjusted);
            if (solarTime.IsValid && tomorrowSolarTime.IsValid)
            {
                return (solarTime, tomorrowSolarTime);
            }

            if (Math.Abs(latitude) < UnsafeLatitude)
            {
                return null;
            }

            latitude -= Math.Sign(latitude) * LatitudeVariationStep;
        }
    }
}
