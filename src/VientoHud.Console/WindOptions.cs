using System.Globalization;
using VientoHud.Core;

namespace VientoHud.ConsoleApp;

/// <summary>Arguments of the <c>wind</c> command.</summary>
internal sealed record WindOptions(Coordinate? Location, int Hours)
{
    public const int DefaultHours = 6;
    public const int MaxHours = 168;

    public const string Usage = "Uso: vientohud wind [--lat <grados> --lon <grados>] [--hours <1-168>]";

    /// <summary>Parses the arguments after the command name. Returns null and an error message when invalid.</summary>
    public static WindOptions? Parse(IReadOnlyList<string> args, out string? error)
    {
        double? lat = null, lon = null;
        var hours = DefaultHours;
        error = null;

        for (var i = 0; i < args.Count; i++)
        {
            var name = args[i];
            if (i + 1 >= args.Count)
            {
                error = $"Falta el valor de {name}.";
                return null;
            }
            var value = args[++i];
            switch (name)
            {
                case "--lat" when TryParseDouble(value, out var v):
                    lat = v;
                    break;
                case "--lon" when TryParseDouble(value, out var v):
                    lon = v;
                    break;
                case "--hours" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var h)
                                    && h is >= 1 and <= MaxHours:
                    hours = h;
                    break;
                case "--lat" or "--lon" or "--hours":
                    error = $"Valor no válido para {name}: {value}";
                    return null;
                default:
                    error = $"Opción desconocida: {name}";
                    return null;
            }
        }

        if (lat.HasValue != lon.HasValue)
        {
            error = "--lat y --lon van juntos.";
            return null;
        }

        Coordinate? location = null;
        if (lat.HasValue)
        {
            try
            {
                location = new Coordinate(lat.Value, lon!.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                error = "Coordenadas fuera de rango (latitud -90..90, longitud -180..180).";
                return null;
            }
        }

        return new WindOptions(location, hours);
    }

    // Accepts both "40.42" and the Spanish "40,42".
    private static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
