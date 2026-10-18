using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using VientoHud.Core.Gpx;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Gpx;

public class GpxWriterTests
{
    private static readonly XNamespace Gpx = GpxWriter.Namespace;
    private static readonly DateTimeOffset Created = new(2026, 10, 14, 15, 30, 0, TimeSpan.Zero);

    private static GpxTrack FullTrack() => new(
        "Vuelta & <León> \"62 km\"",
        [
            new GpxTrackPoint(new Coordinate(42.5987, -5.5671), 838),
            new GpxTrackPoint(new Coordinate(42.6012345, -5.5523456), 841.24),
            new GpxTrackPoint(new Coordinate(42.5987, -5.5671), 838),
        ],
        Created,
        "Empieza hacia el oeste. Ñ, acentos y símbolos: ±10 %",
        GpxCopyright.OpenStreetMap);

    private static GpxTrack MinimalTrack() => new(
        "Minimal",
        [new GpxTrackPoint(new Coordinate(0, 0)), new GpxTrackPoint(new Coordinate(-0.5, 179.9999999))],
        Created);

    [Fact]
    public void Full_track_is_valid_against_the_official_gpx_1_1_schema() =>
        Assert.Empty(SchemaErrors(GpxWriter.ToXml(FullTrack())));

    [Fact]
    public async Task A_recorded_route_becomes_a_valid_track_with_its_points_elevations_and_OpenStreetMap_licence()
    {
        var route = await RecordedRoutes.LeonNorth.LoadAsync();

        var track = GpxTrack.FromRoute(route, "León 17 km", Created, "Vuelta corta");

        Assert.Equal(route.Points.Select(p => (p.Position, p.ElevationMeters)), track.Points.Select(p => (p.Position, p.ElevationMeters)));
        Assert.Equal(GpxCopyright.OpenStreetMap, track.Copyright);
        Assert.Equal("Vuelta corta", track.Description);
        Assert.Empty(SchemaErrors(GpxWriter.ToXml(track)));
    }

    [Fact]
    public async Task Waypoints_go_before_the_track_and_stay_valid()
    {
        var route = await RecordedRoutes.LeonNorth.LoadAsync();
        GpxWaypoint[] waypoints =
        [
            new(route.Points[10].Position, "Lluvia ~11:40", "km 3-8: lluvia probable (80 %)", "Danger Area"),
            new(route.Points[40].Position, "Racha lado 45"),
        ];

        var xml = GpxWriter.ToXml(GpxTrack.FromRoute(route, "Sal 10:30 · 17 km N", Created, "Resumen", waypoints));

        Assert.Empty(SchemaErrors(xml));
        Assert.True(xml.IndexOf("<wpt", StringComparison.Ordinal) < xml.IndexOf("<trk>", StringComparison.Ordinal));
        Assert.Contains("<name>Lluvia ~11:40</name>", xml);
        Assert.Contains("<desc>km 3-8: lluvia probable (80 %)</desc>", xml);
        Assert.Contains("<sym>Danger Area</sym>", xml);
        Assert.Equal(2, xml.Split("<wpt ").Length - 1);
        Assert.Throws<ArgumentException>(() => new GpxWaypoint(route.Points[0].Position, " "));
    }

    [Fact]
    public void Minimal_track_is_valid_against_the_official_gpx_1_1_schema() =>
        Assert.Empty(SchemaErrors(GpxWriter.ToXml(MinimalTrack())));

    [Fact]
    public void Schema_check_catches_an_invalid_document()
    {
        // Guards the guard: a comma decimal separator must fail validation.
        var broken = GpxWriter.ToXml(MinimalTrack()).Replace("lat=\"-0.5\"", "lat=\"-0,5\"");

        Assert.NotEmpty(SchemaErrors(broken));
    }

    [Fact]
    public void Writes_every_point_in_order_with_its_elevation()
    {
        var points = TrackPoints(GpxWriter.ToXml(FullTrack()));

        Assert.Equal(3, points.Count);
        Assert.Equal(("42.5987", "-5.5671", "838"), points[0]);
        Assert.Equal(("42.6012345", "-5.5523456", "841.2"), points[1]);
        Assert.Equal(points[0], points[2]);
    }

    [Fact]
    public void Omits_elevation_when_unknown() =>
        Assert.All(TrackPoints(GpxWriter.ToXml(MinimalTrack())), p => Assert.Null(p.Ele));

    [Fact]
    public void Rounds_coordinates_to_seven_decimals()
    {
        var points = TrackPoints(GpxWriter.ToXml(new GpxTrack(
            "Rounding",
            [new GpxTrackPoint(new Coordinate(40.123456789, -3.987654321)), new GpxTrackPoint(new Coordinate(40, -3))],
            Created)));

        Assert.Equal(("40.1234568", "-3.9876543", null), points[0]);
        Assert.Equal(("40", "-3", null), points[1]);
    }

    [Fact]
    public void Uses_a_dot_as_decimal_separator_whatever_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("es-ES");
            var xml = GpxWriter.ToXml(FullTrack());

            Assert.Equal(("42.6012345", "-5.5523456", "841.2"), TrackPoints(xml)[1]);
            Assert.Empty(SchemaErrors(xml));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Escapes_special_characters_in_texts()
    {
        var gpx = XDocument.Parse(GpxWriter.ToXml(FullTrack()));

        Assert.Equal("Vuelta & <León> \"62 km\"", gpx.Root!.Element(Gpx + "metadata")!.Element(Gpx + "name")!.Value);
        Assert.Equal("Vuelta & <León> \"62 km\"", gpx.Root.Element(Gpx + "trk")!.Element(Gpx + "name")!.Value);
        Assert.Equal("Empieza hacia el oeste. Ñ, acentos y símbolos: ±10 %", gpx.Root.Element(Gpx + "trk")!.Element(Gpx + "desc")!.Value);
    }

    [Fact]
    public void Writes_version_creator_time_and_copyright()
    {
        var root = XDocument.Parse(GpxWriter.ToXml(FullTrack())).Root!;
        var metadata = root.Element(Gpx + "metadata")!;

        Assert.Equal("1.1", root.Attribute("version")!.Value);
        Assert.Equal("VientoHud", root.Attribute("creator")!.Value);
        Assert.Equal("2026-10-14T15:30:00Z", metadata.Element(Gpx + "time")!.Value);
        Assert.Equal("OpenStreetMap contributors", metadata.Element(Gpx + "copyright")!.Attribute("author")!.Value);
        Assert.Equal("https://opendatacommons.org/licenses/odbl/", metadata.Element(Gpx + "copyright")!.Element(Gpx + "license")!.Value);
    }

    [Fact]
    public void Writes_utf8_without_byte_order_mark()
    {
        using var stream = new MemoryStream();
        GpxWriter.Write(FullTrack(), stream);
        var bytes = stream.ToArray();

        Assert.Equal((byte)'<', bytes[0]);
        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", Encoding.UTF8.GetString(bytes));
        Assert.Contains("León", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void Leaves_the_stream_open()
    {
        using var stream = new MemoryStream();
        GpxWriter.Write(MinimalTrack(), stream);

        Assert.True(stream.CanWrite);
    }

    [Fact]
    public void Track_needs_a_name_two_points_and_utc_time()
    {
        GpxTrackPoint[] two = [new(new Coordinate(0, 0)), new(new Coordinate(0, 1))];

        Assert.Throws<ArgumentException>(() => new GpxTrack(" ", two, Created));
        Assert.Throws<ArgumentException>(() => new GpxTrack("One point", [two[0]], Created));
        Assert.Throws<ArgumentException>(() => new GpxTrack("Local time", two, new DateTimeOffset(2026, 10, 14, 17, 30, 0, TimeSpan.FromHours(2))));
    }

    [Fact]
    public void Rejects_non_finite_elevation_and_blank_copyright_author()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpxTrackPoint(new Coordinate(0, 0), double.NaN));
        Assert.Throws<ArgumentException>(() => new GpxCopyright(""));
    }

    private static List<(string Lat, string Lon, string? Ele)> TrackPoints(string xml) =>
        XDocument.Parse(xml).Descendants(Gpx + "trkpt")
            .Select(p => (p.Attribute("lat")!.Value, p.Attribute("lon")!.Value, p.Element(Gpx + "ele")?.Value))
            .ToList();

    private static List<string> SchemaErrors(string xml)
    {
        var schemas = new XmlSchemaSet();
        using (var xsd = XmlReader.Create(new StringReader(Fixture.Read("Gpx/gpx.xsd"))))
            schemas.Add(GpxWriter.Namespace, xsd);

        var errors = new List<string>();
        var settings = new XmlReaderSettings { ValidationType = ValidationType.Schema, Schemas = schemas };
        settings.ValidationFlags |= XmlSchemaValidationFlags.ReportValidationWarnings;
        settings.ValidationEventHandler += (_, e) => errors.Add($"{e.Severity}: {e.Message}");
        using var reader = XmlReader.Create(new StringReader(xml), settings);
        while (reader.Read()) { }
        return errors;
    }
}
