using Microsoft.Extensions.Logging;
using SkiaSharp.Views.Maui.Controls.Hosting;
using VientoHud.App.Maps;
using VientoHud.Core.Net;

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
        builder.Services.AddTransient<MapPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
