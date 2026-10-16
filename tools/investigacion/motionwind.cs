using System.Globalization;
using System.Text.Json;
// dotnet run tools/investigacion/motionwind.cs -- <salida de motioncheck.cs>
// Fase 4, paso 4c-2: compara el movimiento de la lluvia calculado por motioncheck.cs con el viento a 700 hPa (~3.000 m)
// del archivo de previsiones de Open-Meteo (historical-forecast-api, best_match) en el sitio y la hora, que es el que
// suele empujar los chubascos. El viento viene DESDE; la lluvia iría HACIA el rumbo opuesto.
var places = new Dictionary<string, (double Lat, double Lon)>
{
    ["Madrid"] = (40.4168, -3.7038),
    ["León"] = (42.5987, -5.5671),
};

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("VientoHud/0.1");
var winds = new Dictionary<(string Place, string Day), JsonElement>();

Console.WriteLine("sitio,T,lluvia_hacia,lluvia_kmh,viento700_hacia,viento700_kmh,diferencia_grados");
var differences = new List<double>();
var speedRatios = new List<double>();
foreach (var line in File.ReadLines(args[0]).Skip(1))
{
    var f = line.Split(',');
    if (f.Length < 9 || f[2] == "" || !places.TryGetValue(f[0], out var place))
    {
        continue;
    }
    var time = DateTime.ParseExact(f[1], "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    var day = time.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    if (!winds.TryGetValue((f[0], day), out var hourly))
    {
        var url = "https://historical-forecast-api.open-meteo.com/v1/forecast?" +
                  $"latitude={place.Lat.ToString(CultureInfo.InvariantCulture)}&longitude={place.Lon.ToString(CultureInfo.InvariantCulture)}" +
                  $"&hourly=wind_speed_700hPa,wind_direction_700hPa&start_date={day}&end_date={day}&timezone=GMT&wind_speed_unit=kmh";
        using var document = JsonDocument.Parse(await http.GetStringAsync(url));
        hourly = document.RootElement.GetProperty("hourly").Clone();
        winds[(f[0], day)] = hourly;
    }

    // The rain was estimated at T-30 (frames T-60..T-30): the wind of the hour nearest to T-45.
    var hour = (int)Math.Round(time.AddMinutes(-45).TimeOfDay.TotalHours) % 24;
    var from = hourly.GetProperty("wind_direction_700hPa")[hour].GetDouble();
    var speed = hourly.GetProperty("wind_speed_700hPa")[hour].GetDouble();
    var towards = (from + 180) % 360;
    var rainTowards = double.Parse(f[2], CultureInfo.InvariantCulture);
    var rainSpeed = double.Parse(f[3], CultureInfo.InvariantCulture);
    var difference = Math.Abs((rainTowards - towards + 540) % 360 - 180);
    differences.Add(difference);
    speedRatios.Add(rainSpeed / speed);
    Console.WriteLine(string.Join(',', f[0], f[1], f[2], f[3], towards.ToString("0", CultureInfo.InvariantCulture),
        speed.ToString("0", CultureInfo.InvariantCulture), difference.ToString("0", CultureInfo.InvariantCulture)));
}

if (differences.Count > 0)
{
    var sorted = differences.Order().ToList();
    Console.WriteLine();
    Console.WriteLine($"Momentos: {differences.Count}; diferencia de dirección mediana {sorted[sorted.Count / 2]:0}°, " +
                      $"dentro de 45°: {differences.Count(d => d <= 45)}; " +
                      $"velocidad lluvia / viento mediana {speedRatios.Order().ElementAt(speedRatios.Count / 2):0.00}");
}
Console.WriteLine("Weather data by Open-Meteo.com (CC BY 4.0)");
