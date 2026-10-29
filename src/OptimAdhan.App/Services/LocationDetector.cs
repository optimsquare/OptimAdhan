using Windows.Devices.Geolocation;

namespace OptimAdhan.App.Services;

public sealed record DetectedPosition(double Latitude, double Longitude, double AccuracyMeters);

/// <summary>Device position from Windows location services, which honour the user's privacy settings.</summary>
public static class LocationDetector
{
    public static async Task<(DetectedPosition? Position, string? Error)> DetectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var access = await Geolocator.RequestAccessAsync();
            if (access != GeolocationAccessStatus.Allowed)
            {
                return (null, "Location access is turned off. Enable it in Windows Settings › Privacy & security › Location, or search for your city.");
            }

            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15))
                .AsTask(cancellationToken);
            var point = position.Coordinate.Point.Position;
            return (new DetectedPosition(point.Latitude, point.Longitude, position.Coordinate.Accuracy), null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Error("Location detection failed", ex);
            return (null, "Your location could not be detected. Please search for your city instead.");
        }
    }
}
