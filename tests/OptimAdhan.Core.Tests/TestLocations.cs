using OptimAdhan.Core.Location;

namespace OptimAdhan.Core.Tests;

internal static class TestLocations
{
    public static LocationInfo Makkah => LocationInfo.Create("Makkah", 21.4225, 39.8262, "SA", LocationSource.Manual);

    public static LocationInfo NewYork => LocationInfo.Create("New York", 40.7128, -74.006, "US", LocationSource.Manual);

    public static LocationInfo Dhaka => LocationInfo.Create("Dhaka", 23.8103, 90.4125, "BD", LocationSource.Manual);

    public static LocationInfo Sydney => LocationInfo.Create("Sydney", -33.8688, 151.2093, "AU", LocationSource.Manual);

    public static LocationInfo Tromso => LocationInfo.Create("Tromsø", 69.6492, 18.9553, "NO", LocationSource.Manual);
}
