#:project ../../src/VientoHud.Core
using VientoHud.Core;
using VientoHud.Core.Rain;
using VientoHud.Core.Rain.Forecasts;
using VientoHud.Core.Rain.Opera;
// dotnet run tools/investigacion/raintime.cs -- [carpeta caché del radar]
// Fase 7: cuánto tarda la parte que no es BRouter de una recomendación: la lluvia (radar OPERA, AROME y reserva) para
// una salida de 60 km (alcance 35 km) con salidas hasta 2 h después, en Fuenlabrada y León, y cuántos bytes baja.
// Primero sin caché (lo que haría el móvil) y luego con ella.
var cache = args.Length > 0 ? args[0] : Path.Combine(Path.GetTempPath(), "vientohud-raintime-" + Guid.NewGuid().ToString("N")[..8]);
var counting = new CountingHandler { InnerHandler = new HttpClientHandler() };
using var http = new HttpClient(counting) { Timeout = TimeSpan.FromSeconds(120) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var radar = new OperaRadarSource(http, new OperaOptions { CacheDirectory = cache });
var model = new OpenMeteoRainForecastSource(http, OpenMeteoRainOptions.Latest);

(string Name, Coordinate Start)[] places = [("fuenlabrada", new(40.2842, -3.7942)), ("leon", new(42.5987, -5.5671))];
foreach (var pass in new[] { "sin caché", "con caché" })
{
    foreach (var (name, start) in places)
    {
        var now = DateTimeOffset.UtcNow;
        counting.Reset();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var outlook = await RainOutlook.ForecastAsync(radar, model, model, start, 35, now, now.AddHours(4.5));
        Console.WriteLine($"{pass,-9} {name,-12} {clock.Elapsed.TotalSeconds,5:0.0} s, {counting.Requests} peticiones, " +
                          $"{counting.Bytes / 1024.0 / 1024.0:0.0} MB; radar {(outlook.Radar is null ? "no" : outlook.Radar.Time.ToString("HH:mm"))}, " +
                          $"movimiento {(outlook.Motion is null ? "no" : "sí")}, AROME {(outlook.Model is null ? "no" : "sí")}");
    }
}
Console.WriteLine($"Caché en {cache}: {Directory.GetFiles(cache, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024.0 / 1024.0:0.0} MB");

sealed class CountingHandler : DelegatingHandler
{
    public int Requests { get; private set; }
    public long Bytes { get; private set; }
    public void Reset() { Requests = 0; Bytes = 0; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        var response = await base.SendAsync(request, cancellationToken);
        Bytes += (await response.Content.ReadAsByteArrayAsync(cancellationToken)).Length;
        return response;
    }
}
