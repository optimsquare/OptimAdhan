namespace OptimAdhan.Core.Calculation;

// Port of adhan-js (MIT, Batoul Apps) Astronomical.js / MathUtils.js.
// Equations are from Jean Meeus, "Astronomical Algorithms". Keep this file a
// faithful port: parity with adhan-js is verified by the fixture tests.
internal static class Astronomical
{
    public static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    public static double NormalizeToScale(double num, double max) => num - max * Math.Floor(num / max);

    public static double UnwindAngle(double angle) => NormalizeToScale(angle, 360.0);

    public static double QuadrantShiftAngle(double angle)
    {
        if (angle >= -180 && angle <= 180)
        {
            return angle;
        }

        return angle - 360 * JsMath.Round(angle / 360);
    }

    /// <summary>Geometric mean longitude of the sun, in degrees (AA p.163).</summary>
    public static double MeanSolarLongitude(double t) =>
        UnwindAngle(280.4664567 + 36000.76983 * t + 0.0003032 * t * t);

    /// <summary>Geometric mean longitude of the moon, in degrees (AA p.144).</summary>
    public static double MeanLunarLongitude(double t) =>
        UnwindAngle(218.3165 + 481267.8813 * t);

    public static double AscendingLunarNodeLongitude(double t) =>
        UnwindAngle(125.04452 - 1934.136261 * t + 0.0020708 * t * t + Math.Pow(t, 3) / 450000);

    /// <summary>Mean anomaly of the sun (AA p.163).</summary>
    public static double MeanSolarAnomaly(double t) =>
        UnwindAngle(357.52911 + 35999.05029 * t - 0.0001537 * t * t);

    /// <summary>The sun's equation of the center, in degrees (AA p.164).</summary>
    public static double SolarEquationOfTheCenter(double t, double meanAnomaly)
    {
        var mrad = DegreesToRadians(meanAnomaly);
        var term1 = (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(mrad);
        var term2 = (0.019993 - 0.000101 * t) * Math.Sin(2 * mrad);
        var term3 = 0.000289 * Math.Sin(3 * mrad);
        return term1 + term2 + term3;
    }

    /// <summary>Apparent longitude of the sun, referred to the true equinox of the date (AA p.164).</summary>
    public static double ApparentSolarLongitude(double t, double meanLongitude)
    {
        var longitude = meanLongitude + SolarEquationOfTheCenter(t, MeanSolarAnomaly(t));
        var omega = 125.04 - 1934.136 * t;
        var lambda = longitude - 0.00569 - 0.00478 * Math.Sin(DegreesToRadians(omega));
        return UnwindAngle(lambda);
    }

    /// <summary>Mean obliquity of the ecliptic, IAU formula, in degrees (AA p.147).</summary>
    public static double MeanObliquityOfTheEcliptic(double t) =>
        23.439291 - 0.013004167 * t - 0.0000001639 * t * t + 0.0000005036 * Math.Pow(t, 3);

    /// <summary>Mean obliquity corrected for the apparent position of the sun (AA p.165).</summary>
    public static double ApparentObliquityOfTheEcliptic(double t, double meanObliquity)
    {
        var o = 125.04 - 1934.136 * t;
        return meanObliquity + 0.00256 * Math.Cos(DegreesToRadians(o));
    }

    /// <summary>Mean sidereal time, the hour angle of the vernal equinox, in degrees (AA p.165).</summary>
    public static double MeanSiderealTime(double t)
    {
        var jd = t * 36525 + 2451545.0;
        var theta = 280.46061837
            + 360.98564736629 * (jd - 2451545)
            + 0.000387933 * t * t
            - Math.Pow(t, 3) / 38710000;
        return UnwindAngle(theta);
    }

    public static double NutationInLongitude(double solarLongitude, double lunarLongitude, double ascendingNode)
    {
        var term1 = -17.2 / 3600 * Math.Sin(DegreesToRadians(ascendingNode));
        var term2 = 1.32 / 3600 * Math.Sin(2 * DegreesToRadians(solarLongitude));
        var term3 = 0.23 / 3600 * Math.Sin(2 * DegreesToRadians(lunarLongitude));
        var term4 = 0.21 / 3600 * Math.Sin(2 * DegreesToRadians(ascendingNode));
        return term1 - term2 - term3 + term4;
    }

    public static double NutationInObliquity(double solarLongitude, double lunarLongitude, double ascendingNode)
    {
        var term1 = 9.2 / 3600 * Math.Cos(DegreesToRadians(ascendingNode));
        var term2 = 0.57 / 3600 * Math.Cos(2 * DegreesToRadians(solarLongitude));
        var term3 = 0.1 / 3600 * Math.Cos(2 * DegreesToRadians(lunarLongitude));
        var term4 = 0.09 / 3600 * Math.Cos(2 * DegreesToRadians(ascendingNode));
        return term1 + term2 + term3 - term4;
    }

    /// <summary>AA p.93.</summary>
    public static double AltitudeOfCelestialBody(double observerLatitude, double declination, double localHourAngle)
    {
        var term1 = Math.Sin(DegreesToRadians(observerLatitude)) * Math.Sin(DegreesToRadians(declination));
        var term2 = Math.Cos(DegreesToRadians(observerLatitude)) * Math.Cos(DegreesToRadians(declination))
            * Math.Cos(DegreesToRadians(localHourAngle));
        return RadiansToDegrees(Math.Asin(term1 + term2));
    }

    /// <summary>AA p.102.</summary>
    public static double ApproximateTransit(double longitude, double siderealTime, double rightAscension)
    {
        var lw = longitude * -1;
        return NormalizeToScale((rightAscension + lw - siderealTime) / 360, 1);
    }

    /// <summary>Time at which the sun is at its highest point, in hours of universal time (AA p.102).</summary>
    public static double CorrectedTransit(
        double approximateTransit, double longitude, double siderealTime,
        double rightAscension, double previousRightAscension, double nextRightAscension)
    {
        var m0 = approximateTransit;
        var lw = longitude * -1;
        var theta = UnwindAngle(siderealTime + 360.985647 * m0);
        var a = UnwindAngle(InterpolateAngles(rightAscension, previousRightAscension, nextRightAscension, m0));
        var h = QuadrantShiftAngle(theta - lw - a);
        var dm = h / -360;
        return (m0 + dm) * 24;
    }

    /// <summary>AA p.102. Returns NaN when the sun never reaches <paramref name="angle"/>.</summary>
    public static double CorrectedHourAngle(
        double approximateTransit, double angle, Coordinates coordinates, bool afterTransit,
        double siderealTime, double rightAscension, double previousRightAscension, double nextRightAscension,
        double declination, double previousDeclination, double nextDeclination)
    {
        var m0 = approximateTransit;
        var lw = coordinates.Longitude * -1;
        var term1 = Math.Sin(DegreesToRadians(angle))
            - Math.Sin(DegreesToRadians(coordinates.Latitude)) * Math.Sin(DegreesToRadians(declination));
        var term2 = Math.Cos(DegreesToRadians(coordinates.Latitude)) * Math.Cos(DegreesToRadians(declination));
        var h0 = RadiansToDegrees(Math.Acos(term1 / term2));
        var m = afterTransit ? m0 + h0 / 360 : m0 - h0 / 360;
        var theta = UnwindAngle(siderealTime + 360.985647 * m);
        var a = UnwindAngle(InterpolateAngles(rightAscension, previousRightAscension, nextRightAscension, m));
        var delta = Interpolate(declination, previousDeclination, nextDeclination, m);
        var h = theta - lw - a;
        var altitude = AltitudeOfCelestialBody(coordinates.Latitude, delta, h);
        var term3 = altitude - angle;
        var term4 = 360 * Math.Cos(DegreesToRadians(delta)) * Math.Cos(DegreesToRadians(coordinates.Latitude))
            * Math.Sin(DegreesToRadians(h));
        var dm = term3 / term4;
        return (m + dm) * 24;
    }

    /// <summary>Interpolation of a value from equidistant previous and next values (AA p.24).</summary>
    public static double Interpolate(double y2, double y1, double y3, double n)
    {
        var a = y2 - y1;
        var b = y3 - y2;
        var c = b - a;
        return y2 + n / 2 * (a + b + n * c);
    }

    /// <summary>Interpolation of three angles, accounting for angle unwinding (AA p.24).</summary>
    public static double InterpolateAngles(double y2, double y1, double y3, double n)
    {
        var a = UnwindAngle(y2 - y1);
        var b = UnwindAngle(y3 - y2);
        var c = b - a;
        return y2 + n / 2 * (a + b + n * c);
    }

    /// <summary>Julian Day for the given Gregorian date (AA p.60).</summary>
    public static double JulianDay(int year, int month, int day, double hours = 0)
    {
        var y = month > 2 ? year : year - 1;
        var m = month > 2 ? month : month + 12;
        var d = day + hours / 24;
        var a = (int)Math.Truncate(y / 100.0);
        var b = (int)Math.Truncate(2 - a + Math.Truncate(a / 4.0));
        var i0 = (int)Math.Truncate(365.25 * (y + 4716));
        var i1 = (int)Math.Truncate(30.6001 * (m + 1));
        return i0 + i1 + d + b - 1524.5;
    }

    /// <summary>Julian century from the J2000 epoch (AA p.163).</summary>
    public static double JulianCentury(double julianDay) => (julianDay - 2451545.0) / 36525;

    public static bool IsLeapYear(int year) => DateTime.IsLeapYear(year);

    public static DateTime SeasonAdjustedMorningTwilight(double latitude, int dayOfYear, int year, DateTime sunrise)
    {
        var lat = Math.Abs(latitude);
        var a = 75 + 28.65 / 55.0 * lat;
        var b = 75 + 19.44 / 55.0 * lat;
        var c = 75 + 32.74 / 55.0 * lat;
        var d = 75 + 48.1 / 55.0 * lat;

        var adjustment = SeasonalAdjustment(DaysSinceSolstice(dayOfYear, year, latitude), a, b, c, d);
        return JsDate.AddSeconds(sunrise, JsMath.Round(adjustment * -60.0));
    }

    public static DateTime SeasonAdjustedEveningTwilight(double latitude, int dayOfYear, int year, DateTime sunset, Shafaq shafaq)
    {
        var lat = Math.Abs(latitude);
        double a, b, c, d;

        switch (shafaq)
        {
            case Shafaq.Ahmer:
                a = 62 + 17.4 / 55.0 * lat;
                b = 62 - 7.16 / 55.0 * lat;
                c = 62 + 5.12 / 55.0 * lat;
                d = 62 + 19.44 / 55.0 * lat;
                break;
            case Shafaq.Abyad:
                a = 75 + 25.6 / 55.0 * lat;
                b = 75 + 7.16 / 55.0 * lat;
                c = 75 + 36.84 / 55.0 * lat;
                d = 75 + 81.84 / 55.0 * lat;
                break;
            default:
                a = 75 + 25.6 / 55.0 * lat;
                b = 75 + 2.05 / 55.0 * lat;
                c = 75 - 9.21 / 55.0 * lat;
                d = 75 + 6.14 / 55.0 * lat;
                break;
        }

        var adjustment = SeasonalAdjustment(DaysSinceSolstice(dayOfYear, year, latitude), a, b, c, d);
        return JsDate.AddSeconds(sunset, JsMath.Round(adjustment * 60.0));
    }

    private static double SeasonalAdjustment(int dyy, double a, double b, double c, double d)
    {
        if (dyy < 91) return a + (b - a) / 91.0 * dyy;
        if (dyy < 137) return b + (c - b) / 46.0 * (dyy - 91);
        if (dyy < 183) return c + (d - c) / 46.0 * (dyy - 137);
        if (dyy < 229) return d + (c - d) / 46.0 * (dyy - 183);
        if (dyy < 275) return c + (b - c) / 46.0 * (dyy - 229);
        return b + (a - b) / 91.0 * (dyy - 275);
    }

    public static int DaysSinceSolstice(int dayOfYear, int year, double latitude)
    {
        const int northernOffset = 10;
        var southernOffset = IsLeapYear(year) ? 173 : 172;
        var daysInYear = IsLeapYear(year) ? 366 : 365;
        int daysSinceSolstice;

        if (latitude >= 0)
        {
            daysSinceSolstice = dayOfYear + northernOffset;
            if (daysSinceSolstice >= daysInYear)
            {
                daysSinceSolstice -= daysInYear;
            }
        }
        else
        {
            daysSinceSolstice = dayOfYear - southernOffset;
            if (daysSinceSolstice < 0)
            {
                daysSinceSolstice += daysInYear;
            }
        }

        return daysSinceSolstice;
    }
}

/// <summary>JavaScript numeric semantics needed for exact parity with adhan-js.</summary>
internal static class JsMath
{
    /// <summary>JavaScript Math.round: halves round toward positive infinity.</summary>
    public static double Round(double value) => Math.Floor(value + 0.5);
}

/// <summary>
/// JavaScript Date arithmetic: <c>new Date(date.getTime() + seconds * 1000)</c>.
/// The sum is a double that JS truncates to whole milliseconds. Doing the same
/// addition in double precision keeps results identical to adhan-js to the millisecond.
/// </summary>
internal static class JsDate
{
    public static DateTime AddSeconds(DateTime date, double seconds)
    {
        var epochMs = (double)((date.Ticks - DateTime.UnixEpoch.Ticks) / TimeSpan.TicksPerMillisecond);
        var resultMs = (long)Math.Truncate(epochMs + seconds * 1000);
        return DateTime.UnixEpoch.AddTicks(resultMs * TimeSpan.TicksPerMillisecond);
    }

    public static DateTime AddMinutes(DateTime date, double minutes) => AddSeconds(date, minutes * 60);
}
