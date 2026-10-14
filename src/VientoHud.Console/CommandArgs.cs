using System.Globalization;
using VientoHud.Core;

namespace VientoHud.ConsoleApp;

/// <summary>Reads "--name value" pairs. Interpreting the values is up to each command's options.</summary>
internal static class CommandArgs
{
    /// <summary>Returns null and an error message on an unknown option, a missing value or a repeated option.</summary>
    public static Dictionary<string, string>? Parse(IReadOnlyList<string> args, IReadOnlySet<string> known, out string? error)
    {
        var values = new Dictionary<string, string>();
        error = null;
        for (var i = 0; i < args.Count; i += 2)
        {
            var name = args[i];
            if (!known.Contains(name))
            {
                error = $"Opción desconocida: {name}";
                return null;
            }
            if (i + 1 >= args.Count)
            {
                error = $"Falta el valor de {name}.";
                return null;
            }
            if (!values.TryAdd(name, args[i + 1]))
            {
                error = $"{name} aparece dos veces.";
                return null;
            }
        }
        return values;
    }

    /// <summary>Optional --lat/--lon pair. Returns false with an error when only one is given or a value is invalid.</summary>
    public static bool TryGetLocation(Dictionary<string, string> values, out Coordinate? location, out string? error)
    {
        location = null;
        error = null;
        var hasLat = values.TryGetValue("--lat", out var latText);
        var hasLon = values.TryGetValue("--lon", out var lonText);
        if (hasLat != hasLon)
        {
            error = "--lat y --lon van juntos.";
            return false;
        }
        if (!hasLat)
        {
            return true;
        }
        if (!TryParseDouble(latText!, out var lat) || !TryParseDouble(lonText!, out var lon))
        {
            error = $"Coordenadas no válidas: {latText}, {lonText}";
            return false;
        }
        try
        {
            location = new Coordinate(lat, lon);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            error = "Coordenadas fuera de rango (latitud -90..90, longitud -180..180).";
            return false;
        }
    }

    /// <summary>Optional integer in [min, max]; <paramref name="value"/> keeps its default when absent.</summary>
    public static bool TryGetInt(
        Dictionary<string, string> values, string name, int min, int max, ref int value, out string? error)
    {
        error = null;
        if (!values.TryGetValue(name, out var text))
        {
            return true;
        }
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max)
        {
            value = parsed;
            return true;
        }
        error = $"Valor no válido para {name}: {text} (entre {min} y {max}).";
        return false;
    }

    // Accepts both "40.42" and the Spanish "40,42".
    public static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
