using System.Text.Json;
using VientoHud.Core.Flyover;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Flyover;

public class FlyoverSceneTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);

    // A straight road north with a point every 100 m: 2 km flat, then 2 km at 6 %.
    private static Route Road()
    {
        var points = Enumerable.Range(0, 41)
            .Select(s => Leon.Destination(0, s * 0.1))
            .Select(p => new RoutePoint(p, 800 + Math.Max(0, Leon.DistanceKmTo(p) - 2) * 60))
            .ToList();
        return new Route(points, [], 4, null, "Test");
    }

    [Fact]
    public void One_piece_per_stretch_of_the_profile_cut_exactly_where_the_band_changes()
    {
        var route = Road();
        var profile = RouteProfile.From(route)!;

        var scene = FlyoverScene.From(route, profile);

        Assert.Equal(profile.Stretches.Count, scene.Pieces.Count);
        Assert.Equal(profile.Stretches.Select(s => (GradeBand?)s.Band), scene.Pieces.Select(p => p.Band));
        Assert.Equal(route.Points[0].Position, scene.Pieces[0].Points[0]);
        Assert.Equal(route.Points[^1].Position, scene.Pieces[^1].Points[^1]);
        // Each piece ends where the next begins, at its stretch's km.
        foreach (var (piece, next) in scene.Pieces.Zip(scene.Pieces.Skip(1)))
        {
            Assert.Equal(piece.Points[^1], next.Points[0]);
            Assert.Equal(piece.EndKm, Leon.DistanceKmTo(piece.Points[^1]), 3);
        }
        Assert.Equal(route.Points[0].Position, scene.Start);
        Assert.Equal(4, scene.LengthKm, 3);
        Assert.Equal(Leon.Latitude, scene.South, 6);
        Assert.Equal(route.Points[^1].Position.Latitude, scene.North, 6);
        Assert.Equal(Leon.Longitude, scene.West, 6);
        Assert.Equal(Leon.Longitude, scene.East, 6);
        Assert.All(scene.Pieces, p => Assert.Equal(GradeBands.Color(p.Band!.Value), p.Color));
    }

    [Fact]
    public void Without_a_profile_the_whole_track_is_one_piece_without_colour()
    {
        var route = Road();

        var scene = FlyoverScene.From(route, null);

        var piece = Assert.Single(scene.Pieces);
        Assert.Null(piece.Band);
        Assert.Null(piece.Color);
        Assert.Equal(route.Points.Count, piece.Points.Count);
        Assert.Equal(0, piece.StartKm);
        Assert.Equal(4, piece.EndKm, 3);
    }

    [Fact]
    public void Writes_geojson_the_view_can_read()
    {
        var route = Road();

        var json = FlyoverScene.From(route, RouteProfile.From(route)).ToJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var features = root.GetProperty("pieces").GetProperty("features");
        Assert.Equal("FeatureCollection", root.GetProperty("pieces").GetProperty("type").GetString());
        Assert.True(features.GetArrayLength() >= 2);
        var first = features[0];
        Assert.Equal("LineString", first.GetProperty("geometry").GetProperty("type").GetString());
        var firstPoint = first.GetProperty("geometry").GetProperty("coordinates")[0];
        // [longitude, latitude], as GeoJSON wants them.
        Assert.Equal(Leon.Longitude, firstPoint[0].GetDouble(), 5);
        Assert.Equal(Leon.Latitude, firstPoint[1].GetDouble(), 5);
        Assert.Matches("^#[0-9A-F]{6}$", first.GetProperty("properties").GetProperty("color").GetString());
        Assert.Equal("Flat", first.GetProperty("properties").GetProperty("band").GetString());
        Assert.Equal(Leon.Longitude, root.GetProperty("start")[0].GetDouble(), 5);
        Assert.Equal(Leon.Latitude, root.GetProperty("bounds")[0][1].GetDouble(), 5);
        Assert.Equal(4, root.GetProperty("lengthKm").GetDouble(), 2);
    }

    [Fact]
    public async Task A_recorded_route_gives_a_scene_with_the_route_length()
    {
        var route = await RecordedRoutes.LeonNorth.LoadAsync();

        var scene = FlyoverScene.From(route, RouteProfile.From(route));

        Assert.Equal(route.DistanceKm, scene.LengthKm, 0);
        Assert.Equal(route.Points[0].Position, scene.Start);
        Assert.True(scene.North > scene.South && scene.East > scene.West);
        Assert.All(scene.Pieces, p => Assert.True(p.Points.Count >= 2));
    }
}
