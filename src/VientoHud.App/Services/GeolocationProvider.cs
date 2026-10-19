using VientoHud.Core.Location;
using GeoCoordinate = VientoHud.Core.Coordinate;

namespace VientoHud.App.Services;

/// <summary>Core's <see cref="ILocationProvider"/> over MAUI's <see cref="Geolocation"/>: asks for the permission the first time.</summary>
public sealed class GeolocationProvider : ILocationProvider
{
    private static readonly TimeSpan FixTimeout = TimeSpan.FromSeconds(15);

    public async Task<LocationFix> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        }
        if (status != PermissionStatus.Granted)
        {
            throw new LocationUnavailableException("Sin permiso de ubicación: dáselo a VientoHud en los ajustes del móvil.");
        }

        Location? location;
        try
        {
            // The last fix at once, then a fresh one worth a few seconds: a stale fix may be another town.
            location = await Geolocation.Default.GetLastKnownLocationAsync();
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, FixTimeout);
            location = await Geolocation.Default.GetLocationAsync(request, cancellationToken) ?? location;
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or FeatureNotEnabledException or PermissionException)
        {
            throw new LocationUnavailableException($"El móvil no da la ubicación: {ex.Message}", ex);
        }
        if (location is null)
        {
            throw new LocationUnavailableException("El móvil no encuentra la posición. ¿Está activada la ubicación?");
        }
        return new LocationFix(new GeoCoordinate(location.Latitude, location.Longitude), location.Accuracy, location.Timestamp.ToUniversalTime());
    }
}
