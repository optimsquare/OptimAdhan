namespace OptimAdhan.Core.Calculation;

/// <summary>Geographic coordinates in decimal degrees (north and east positive).</summary>
public readonly record struct Coordinates(double Latitude, double Longitude);

// Port of adhan-js SolarCoordinates.js.
internal sealed class SolarCoordinates
{
    public SolarCoordinates(double julianDay)
    {
        var t = Astronomical.JulianCentury(julianDay);
        var l0 = Astronomical.MeanSolarLongitude(t);
        var lp = Astronomical.MeanLunarLongitude(t);
        var omega = Astronomical.AscendingLunarNodeLongitude(t);
        var lambda = Astronomical.DegreesToRadians(Astronomical.ApparentSolarLongitude(t, l0));
        var theta0 = Astronomical.MeanSiderealTime(t);
        var dPsi = Astronomical.NutationInLongitude(l0, lp, omega);
        var dEpsilon = Astronomical.NutationInObliquity(l0, lp, omega);
        var epsilon0 = Astronomical.MeanObliquityOfTheEcliptic(t);
        var epsilonApparent = Astronomical.DegreesToRadians(Astronomical.ApparentObliquityOfTheEcliptic(t, epsilon0));

        // AA p.165
        Declination = Astronomical.RadiansToDegrees(Math.Asin(Math.Sin(epsilonApparent) * Math.Sin(lambda)));

        // AA p.165
        RightAscension = Astronomical.UnwindAngle(Astronomical.RadiansToDegrees(
            Math.Atan2(Math.Cos(epsilonApparent) * Math.Sin(lambda), Math.Cos(lambda))));

        // AA p.88
        ApparentSiderealTime = theta0
            + dPsi * 3600 * Math.Cos(Astronomical.DegreesToRadians(epsilon0 + dEpsilon)) / 3600;
    }

    /// <summary>Angle between the sun's rays and the plane of the earth's equator, in degrees.</summary>
    public double Declination { get; }

    /// <summary>Angular distance on the celestial equator from the vernal equinox to the hour circle, in degrees.</summary>
    public double RightAscension { get; }

    /// <summary>Hour angle of the vernal equinox, in degrees.</summary>
    public double ApparentSiderealTime { get; }
}

// Port of adhan-js SolarTime.js. Times are fractional hours of UTC on the given date.
internal sealed class SolarTime
{
    private readonly SolarCoordinates _solar;
    private readonly SolarCoordinates _prevSolar;
    private readonly SolarCoordinates _nextSolar;
    private readonly double _approxTransit;

    public SolarTime(DateOnly date, Coordinates coordinates)
    {
        var julianDay = Astronomical.JulianDay(date.Year, date.Month, date.Day);
        Observer = coordinates;
        _solar = new SolarCoordinates(julianDay);
        _prevSolar = new SolarCoordinates(julianDay - 1);
        _nextSolar = new SolarCoordinates(julianDay + 1);

        _approxTransit = Astronomical.ApproximateTransit(
            coordinates.Longitude, _solar.ApparentSiderealTime, _solar.RightAscension);
        const double solarAltitude = -50.0 / 60.0;

        Transit = Astronomical.CorrectedTransit(
            _approxTransit, coordinates.Longitude, _solar.ApparentSiderealTime,
            _solar.RightAscension, _prevSolar.RightAscension, _nextSolar.RightAscension);
        Sunrise = HourAngle(solarAltitude, afterTransit: false);
        Sunset = HourAngle(solarAltitude, afterTransit: true);
    }

    public Coordinates Observer { get; }

    public double Transit { get; }

    public double Sunrise { get; }

    public double Sunset { get; }

    public bool IsValid => !double.IsNaN(Sunrise) && !double.IsNaN(Sunset);

    public double HourAngle(double angle, bool afterTransit) =>
        Astronomical.CorrectedHourAngle(
            _approxTransit, angle, Observer, afterTransit, _solar.ApparentSiderealTime,
            _solar.RightAscension, _prevSolar.RightAscension, _nextSolar.RightAscension,
            _solar.Declination, _prevSolar.Declination, _nextSolar.Declination);

    public double Afternoon(double shadowLength)
    {
        var tangent = Math.Abs(Observer.Latitude - _solar.Declination);
        var inverse = shadowLength + Math.Tan(Astronomical.DegreesToRadians(tangent));
        var angle = Astronomical.RadiansToDegrees(Math.Atan(1.0 / inverse));
        return HourAngle(angle, afterTransit: true);
    }

    /// <summary>
    /// Port of adhan-js TimeComponents.utcDate: converts fractional UTC hours on
    /// <paramref name="date"/> to an instant. Returns null for NaN (event does not occur).
    /// Hours may be negative or exceed 24; the overflow rolls into adjacent days.
    /// </summary>
    public static DateTime? ToUtc(double hoursValue, DateOnly date)
    {
        if (double.IsNaN(hoursValue) || double.IsInfinity(hoursValue))
        {
            return null;
        }

        var hours = Math.Floor(hoursValue);
        var minutes = Math.Floor((hoursValue - hours) * 60);
        var seconds = Math.Floor((hoursValue - (hours + minutes / 60)) * 60 * 60);

        return new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Utc)
            .AddHours(hours).AddMinutes(minutes).AddSeconds(seconds);
    }
}
