using System.Globalization;
using System.Text;
using System.Xml;

namespace VientoHud.Core.Gpx;

/// <summary>
/// Writes GPX 1.1 files (https://www.topografix.com/GPX/1/1/) with one track and one segment,
/// the form bike GPS units (Garmin, Wahoo…) import as a course.
/// </summary>
public static class GpxWriter
{
    public const string Namespace = "http://www.topografix.com/GPX/1/1";
    public const string Creator = "VientoHud";

    private const string SchemaInstanceNamespace = "http://www.w3.org/2001/XMLSchema-instance";
    private const string SchemaLocation = Namespace + " https://www.topografix.com/GPX/1/1/gpx.xsd";

    // 7 decimals is about 1 cm; more is noise. Elevation to the decimetre.
    private const string CoordinateFormat = "0.#######";
    private const string ElevationFormat = "0.#";

    /// <summary>Writes the track and its waypoints as UTF-8 (no byte order mark). Leaves the stream open.</summary>
    public static void Write(GpxTrack track, Stream output)
    {
        ArgumentNullException.ThrowIfNull(track);
        ArgumentNullException.ThrowIfNull(output);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            CloseOutput = false,
        };
        using var xml = XmlWriter.Create(output, settings);

        xml.WriteStartDocument();
        xml.WriteStartElement("gpx", Namespace);
        xml.WriteAttributeString("version", "1.1");
        xml.WriteAttributeString("creator", Creator);
        xml.WriteAttributeString("xmlns", "xsi", null, SchemaInstanceNamespace);
        xml.WriteAttributeString("schemaLocation", SchemaInstanceNamespace, SchemaLocation);

        // Element order is fixed by the schema: metadata (name, desc, author, copyright, link, time), then trk.
        xml.WriteStartElement("metadata", Namespace);
        xml.WriteElementString("name", Namespace, track.Name);
        if (track.Description is { } description)
            xml.WriteElementString("desc", Namespace, description);
        if (track.Copyright is { } copyright)
        {
            xml.WriteStartElement("copyright", Namespace);
            xml.WriteAttributeString("author", copyright.Author);
            if (copyright.License is { } license)
                xml.WriteElementString("license", Namespace, license.AbsoluteUri);
            xml.WriteEndElement();
        }
        xml.WriteElementString("time", Namespace, track.Time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture));
        xml.WriteEndElement();

        // Waypoints go between metadata and trk; inside, name, desc and sym in that order.
        foreach (var waypoint in track.Waypoints)
        {
            xml.WriteStartElement("wpt", Namespace);
            xml.WriteAttributeString("lat", waypoint.Position.Latitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture));
            xml.WriteAttributeString("lon", waypoint.Position.Longitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture));
            xml.WriteElementString("name", Namespace, waypoint.Name);
            if (waypoint.Description is { } waypointDescription)
                xml.WriteElementString("desc", Namespace, waypointDescription);
            if (waypoint.Symbol is { } symbol)
                xml.WriteElementString("sym", Namespace, symbol);
            xml.WriteEndElement();
        }

        // trk: name, desc, then the segment.
        xml.WriteStartElement("trk", Namespace);
        xml.WriteElementString("name", Namespace, track.Name);
        if (track.Description is { } trackDescription)
            xml.WriteElementString("desc", Namespace, trackDescription);
        xml.WriteStartElement("trkseg", Namespace);
        foreach (var point in track.Points)
        {
            xml.WriteStartElement("trkpt", Namespace);
            xml.WriteAttributeString("lat", point.Position.Latitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture));
            xml.WriteAttributeString("lon", point.Position.Longitude.ToString(CoordinateFormat, CultureInfo.InvariantCulture));
            if (point.ElevationMeters is { } elevation)
                xml.WriteElementString("ele", Namespace, elevation.ToString(ElevationFormat, CultureInfo.InvariantCulture));
            xml.WriteEndElement();
        }
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteEndElement();
        xml.WriteEndDocument();
    }

    /// <summary>The GPX document as a string.</summary>
    public static string ToXml(GpxTrack track)
    {
        using var buffer = new MemoryStream();
        Write(track, buffer);
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
