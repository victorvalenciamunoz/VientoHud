using VientoHud.Core;
using VientoHud.Core.Location;
using Windows.Devices.Geolocation;

namespace VientoHud.ConsoleApp;

/// <summary>Device position from the Windows location service (GPS, Wi-Fi or IP, whatever Windows has).</summary>
internal sealed class WindowsLocationProvider : ILocationProvider
{
    private static readonly TimeSpan MaximumAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public async Task<LocationFix> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var access = await Geolocator.RequestAccessAsync().AsTask(cancellationToken);
        if (access != GeolocationAccessStatus.Allowed)
        {
            throw new LocationUnavailableException(
                $"Windows denied access to the location ({access}). " +
                "Enable it in Settings > Privacy & security > Location, or pass --lat and --lon.");
        }

        try
        {
            var locator = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var position = await locator.GetGeopositionAsync(MaximumAge, Timeout).AsTask(cancellationToken);
            var point = position.Coordinate.Point.Position;
            return new LocationFix(
                new Coordinate(point.Latitude, point.Longitude),
                position.Coordinate.Accuracy,
                position.Coordinate.Timestamp.ToUniversalTime());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new LocationUnavailableException($"Windows could not get the location: {ex.Message}", ex);
        }
    }
}
