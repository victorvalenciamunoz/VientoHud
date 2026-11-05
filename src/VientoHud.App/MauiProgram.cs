using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using VientoHud.App.Maps;
using VientoHud.App.Services;
using VientoHud.Core.Flyover;
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

        // What the app takes from each service, kept on the device (phase 9, DECISIONS 059), and one client for every
        // service, with the retries of DECISIONS 049 and that count.
        builder.Services.AddSingleton(_ => new UsageMeter(Path.Combine(FileSystem.AppDataDirectory, "usage.json")));
        // A recorded day (development builds with VIENTOHUD_APP_REPLAY, phase 9 step 4): the clock, the place and every
        // source as they were then; each answer kept on the device, so living it again needs no network.
        var replay = AppSettings.Replay;
        var clock = replay?.Clock() ?? TimeProvider.System;
        builder.Services.AddSingleton(clock);
        builder.Services.AddSingleton(sp => replay is null
            ? ResilientHttp.CreateClient(meter: sp.GetRequiredService<UsageMeter>())
            : ResilientHttp.CreateClient(new RecordingHandler(Path.Combine(FileSystem.AppDataDirectory, "replay", $"{replay.Start:yyyyMMdd'T'HHmm}"))
            {
                InnerHandler = new MeteredHandler(sp.GetRequiredService<UsageMeter>()) { InnerHandler = new HttpClientHandler() },
            }));
        builder.Services.AddSingleton<TileSources>();
        // The 3D view's tiles (terrain and base map) kept on the device, so the view works offline once seen (phase 8).
        builder.Services.AddSingleton(sp => new TileStore(sp.GetRequiredService<HttpClient>(), Path.Combine(FileSystem.CacheDirectory, "flyover")));

        // Core: the wind and where to head (7d). The device: where the rider is.
        builder.Services.AddSingleton<IWindForecastProvider>(sp => replay?.Wind(sp.GetRequiredService<HttpClient>())
            ?? new OpenMeteoWindForecastProvider(sp.GetRequiredService<HttpClient>()));
        builder.Services.AddSingleton<DirectionAdvisor>();
        if (replay is null)
        {
            builder.Services.AddSingleton<ILocationProvider, GeolocationProvider>();
        }
        else
        {
            builder.Services.AddSingleton(replay.Location(clock));
        }

        // Core: the ride traced on the map (7e): BRouter (brouter.de, one request a second; a development server when
        // built with VIENTOHUD_APP_BROUTER_URL), the radar kept 3 h on the device, and the rain reused for 10 min (DECISIONS 049).
        // The points touched are the point: the profile that keeps the detours to them (DECISIONS 051).
        builder.Services.AddSingleton<IRouteEngine>(sp => new BRouterRouteEngine(
            sp.GetRequiredService<HttpClient>(),
            (AppSettings.BRouterUrl is { } url ? new BRouterOptions { BaseUri = url, MinRequestInterval = TimeSpan.Zero } : new BRouterOptions())
                with { ProfileText = BRouterRouteEngine.StrictRoadProfileKeepingViaPoints }));
        builder.Services.AddSingleton<IRadarSource>(sp =>
        {
            var opera = new OperaRadarSource(
                sp.GetRequiredService<HttpClient>(),
                new OperaOptions
                {
                    CacheDirectory = Path.Combine(FileSystem.CacheDirectory, "radar"),
                    CacheMaxAge = TimeSpan.FromHours(3),
                    // Only the parts of each composite the crop needs (phase 9, DECISIONS 061): a tenth of the data.
                    PartialReads = true,
                });
            return replay?.Radar(opera, clock) ?? opera;
        });
        builder.Services.AddSingleton<IRainOutlookSource>(sp =>
        {
            var http = sp.GetRequiredService<HttpClient>();
            if (replay is not null)
            {
                return new CachedRainOutlookSource(new RainOutlookSource(sp.GetRequiredService<IRadarSource>(), replay.RainModel(http), replay.RainFallback(http)));
            }
            var forecasts = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.Latest);
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
