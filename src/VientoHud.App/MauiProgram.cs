using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using VientoHud.App.Maps;
using VientoHud.App.Services;
using VientoHud.Core.Location;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;
using VientoHud.Core.Net;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Opera;
using VientoHud.Core.Routing.BRouter;

namespace VientoHud.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            // Mapsui draws with SkiaSharp; without this the first map crashes the app.
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("ChakraPetch-Regular.ttf", "ChakraPetch");
                fonts.AddFont("ChakraPetch-Bold.ttf", "ChakraPetchBold");
            });

        // One client for every service, with the retries of DECISIONS 049.
        builder.Services.AddSingleton(_ => ResilientHttp.CreateClient());
        builder.Services.AddSingleton<TileSources>();

        // Core: the wind and where to head (7d). The device: where the rider is.
        builder.Services.AddSingleton<IWindForecastProvider>(sp => new OpenMeteoWindForecastProvider(sp.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton<DirectionAdvisor>();
        builder.Services.AddSingleton<ILocationProvider, GeolocationProvider>();

        // Core: the ride traced on the map (7e): BRouter (brouter.de, one request a second; a development server when
        // built with VIENTOHUD_APP_BROUTER_URL), the radar kept 3 h on the device, and the rain reused for 10 min (DECISIONS 049).
        builder.Services.AddSingleton<IRouteEngine>(sp => new BRouterRouteEngine(
            sp.GetRequiredService<HttpClient>(),
            AppSettings.BRouterUrl is { } url ? new BRouterOptions { BaseUri = url, MinRequestInterval = TimeSpan.Zero } : new BRouterOptions()));
        builder.Services.AddSingleton<IRadarSource>(sp => new OperaRadarSource(
            sp.GetRequiredService<HttpClient>(),
            new OperaOptions { CacheDirectory = Path.Combine(FileSystem.CacheDirectory, "radar"), CacheMaxAge = TimeSpan.FromHours(3) }));
        builder.Services.AddSingleton<IRainOutlookSource>(sp =>
        {
            var forecasts = new OpenMeteoRainForecastSource(sp.GetRequiredService<HttpClient>(), OpenMeteoRainOptions.Latest);
            return new CachedRainOutlookSource(new RainOutlookSource(sp.GetRequiredService<IRadarSource>(), forecasts, forecasts));
        });
        builder.Services.AddSingleton<TracedRideAdvisor>();
        builder.Services.AddTransient<MapPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
