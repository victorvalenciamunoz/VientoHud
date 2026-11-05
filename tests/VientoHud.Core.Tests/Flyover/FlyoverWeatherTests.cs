using VientoHud.Core.Tests.Support;
using System.Text.Json;
using VientoHud.Core.Flyover;
using VientoHud.Core.Rain;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Flyover;

public class FlyoverWeatherTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    // 10 km north from the origin in 1 km legs of 2.4 min, a 20 km/h wind from the north (in the face); likely rain on
    // km 6-8, gusts on km 2-4.
    private static RideOption Ride()
    {
        var legs = Enumerable.Range(0, 10).Select(i => new RouteLeg(
            i, i, 1, Origin.Destination(0, i), Origin.Destination(0, i + 1), Origin.Destination(0, i + 0.5), 0, 1, null,
            Eight.AddMinutes(2.4 * i), Eight.AddMinutes(2.4 * (i + 1)))).ToList();
        var winds = legs.Select(l => new LegWind(l, new LocalWind(20, 45, 0, "fake"))).ToList();
        var rain = legs.Select(l => new LegRain(l, new PlaceRain(l.StartKm is >= 6 and < 8 ? 0.8 : 0, RainSourceKind.Radar))).ToList();
        var route = new Route([new RoutePoint(Origin), new RoutePoint(Origin.Destination(0, 10))], [], 10, null, "test");
        return new RideOption(0, route, false, winds, new RideScore(0, 0, [new GustyStretch(2, 4, 45)], true), new RideRain(rain, null));
    }

    [Fact]
    public void Puts_short_arrows_close_together_with_the_wind_at_the_time_of_passing()
    {
        var weather = FlyoverWeather.From(Ride(), withAlerts: true);

        // At km 1,25, 3,75, 6,25 and 8,75.
        Assert.Equal([1.25, 3.75, 6.25, 8.75], weather.Arrows.Select(a => a.AtKm));
        Assert.All(weather.Arrows, a => Assert.Equal(WindFeel.Headwind, a.Feel));
        Assert.All(weather.Arrows, a => Assert.Equal(FlyoverWeather.ArrowLengthKm, a.Tail.DistanceKmTo(a.Tip), 2));
        // For the close view, one every km (at 0,5, 1,5… 9,5), short and next to the road.
        Assert.Equal(10, weather.CloseArrows.Count);
        Assert.Equal(0.5, weather.CloseArrows[0].AtKm);
        Assert.All(weather.CloseArrows, a => Assert.Equal(FlyoverWeather.CloseArrowLengthKm, a.Tail.DistanceKmTo(a.Tip), 2));
        // The road goes north from the origin: 100 m to its right is 100 m east.
        var middle = Origin.Destination(0, 0.5);
        var centre = weather.CloseArrows[0].Tail.Destination(weather.CloseArrows[0].Tail.BearingTo(weather.CloseArrows[0].Tip), FlyoverWeather.CloseArrowLengthKm / 2);
        Assert.Equal(FlyoverWeather.CloseArrowSideKm, middle.DistanceKmTo(centre), 2);
        Assert.Equal([RideAlertKind.Gusts, RideAlertKind.Rain], weather.Alerts.Select(a => a.Kind));
    }

    [Fact]
    public void Marks_the_way_round_every_km_colored_by_feel_with_a_drop_where_it_rains()
    {
        var weather = FlyoverWeather.From(Ride(), withAlerts: true);

        // At km 0,5, 1,5… 9,5, heading north into the wind.
        Assert.Equal(10, weather.Marks.Count);
        Assert.Equal(0.5, weather.Marks[0].AtKm);
        Assert.All(weather.Marks, m => Assert.Equal((WindFeel.Headwind, 0.0), (m.Feel, Math.Round(m.BearingDegrees))));
        // Rain likely on km 6-8: the marks at 6,5 and 7,5.
        Assert.Equal([6.5, 7.5], weather.Marks.Where(m => m.Rain).Select(m => m.AtKm));
        // The drop 60 m to the rider's right: east, going north.
        var wet = weather.Marks.First(m => m.Rain);
        Assert.Equal(FlyoverWeather.DropSideKm, wet.Position.DistanceKmTo(wet.Drop), 3);
        Assert.Equal(90, wet.Position.BearingTo(wet.Drop), 0);
        // Before the rain is in, no drops.
        Assert.DoesNotContain(FlyoverWeather.From(Ride(), withAlerts: false).Marks, m => m.Rain);
    }

    [Fact]
    public void On_an_out_and_back_the_same_wind_is_in_the_face_going_and_behind_coming_back()
    {
        // 5 km east and back, a point every 100 m; a steady 20 km/h wind from the east.
        var points = Enumerable.Range(0, 51).Select(i => new RoutePoint(Origin.Destination(90, i * 0.1))).ToList();
        var route = new Route(points, [], 5, null, "test").ThereAndBack();
        var legs = RouteLegs.Split(route, Eight);
        var winds = legs.Select(l => new LegWind(l, new LocalWind(20, null, 90, "fake"))).ToList();
        var option = new RideOption(0, route, false, winds, new RideScore(0, 0, [], true));

        var weather = FlyoverWeather.From(option, withAlerts: false);

        foreach (var arrows in new[] { weather.Arrows, weather.CloseArrows })
        {
            Assert.All(arrows.Where(a => a.AtKm < 4.5), a => Assert.Equal(WindFeel.Headwind, a.Feel));
            Assert.All(arrows.Where(a => a.AtKm > 5.5), a => Assert.Equal(WindFeel.Tailwind, a.Feel));
            // Each way's arrows stand on its own right-hand side: the way out's south of the road, the way back's north.
            Assert.All(arrows.Where(a => a.AtKm < 4.5), a => Assert.True(a.Tip.Latitude < Origin.Latitude));
            Assert.All(arrows.Where(a => a.AtKm > 5.5), a => Assert.True(a.Tip.Latitude > Origin.Latitude));
        }
        Assert.Equal(WindFeel.Headwind, weather.At(2).Feel);
        Assert.Equal(WindFeel.Tailwind, weather.At(8).Feel);
    }

    [Fact]
    public void Says_what_the_rider_meets_at_each_point()
    {
        var weather = FlyoverWeather.From(Ride(), withAlerts: true);

        var start = weather.At(0.5);
        Assert.Equal(WindFeel.Headwind, start.Feel);
        Assert.Equal(Eight.AddMinutes(1.2), start.Time!.Value, TimeSpan.FromSeconds(1));
        Assert.Null(start.Alert);
        Assert.Equal(RideAlertKind.Gusts, weather.At(3).Alert!.Kind);
        Assert.Equal(RideAlertKind.Rain, weather.At(7).Alert!.Kind);
        Assert.Null(weather.At(9).Alert);
        Assert.Equal(Eight.AddMinutes(24), weather.At(10).Time);
    }

    [Fact]
    public void Without_the_rain_there_are_no_alerts_and_before_the_wind_nothing()
    {
        Assert.Empty(FlyoverWeather.From(Ride(), withAlerts: false).Alerts);
        Assert.NotEmpty(FlyoverWeather.From(Ride(), withAlerts: false).Arrows);

        var none = FlyoverWeather.None.At(3);
        Assert.Null(none.Wind);
        Assert.Null(none.Time);
        Assert.Null(none.Alert);
    }

    [Fact]
    public void Writes_geojson_the_view_can_draw()
    {
        using var doc = JsonDocument.Parse(FlyoverWeather.From(Ride(), withAlerts: true).ToJson());

        var arrows = doc.RootElement.GetProperty("arrows").GetProperty("features");
        Assert.Equal(4, arrows.GetArrayLength());
        Assert.Equal("MultiLineString", arrows[0].GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal(2, arrows[0].GetProperty("geometry").GetProperty("coordinates").GetArrayLength());
        Assert.Equal("Headwind", arrows[0].GetProperty("properties").GetProperty("feel").GetString());
        Assert.Equal(10, doc.RootElement.GetProperty("closeArrows").GetProperty("features").GetArrayLength());
        var marks = doc.RootElement.GetProperty("marks").GetProperty("features");
        Assert.Equal(10, marks.GetArrayLength());
        Assert.Equal("Point", marks[0].GetProperty("geometry").GetProperty("type").GetString());
        Assert.Equal("Headwind", marks[0].GetProperty("properties").GetProperty("feel").GetString());
        Assert.Equal(0, marks[0].GetProperty("properties").GetProperty("bearing").GetDouble(), 0);
        var drops = doc.RootElement.GetProperty("drops").GetProperty("features");
        Assert.Equal(2, drops.GetArrayLength());
        Assert.Equal(6.5, drops[0].GetProperty("properties").GetProperty("atKm").GetDouble());
    }

    [Fact]
    public void Writes_the_line_under_the_flight()
    {
        var weather = FlyoverWeather.From(Ride(), withAlerts: true);

        Assert.Equal("De cara 20 km/h · pasas hacia las 10:01", RideText.FlyoverWeatherAt(weather.At(0.5), Zone));
        Assert.Equal("De cara 20 km/h · pasas hacia las 10:07 · rachas de lado de hasta 45 km/h", RideText.FlyoverWeatherAt(weather.At(3), Zone));
        Assert.Equal("De cara 20 km/h · pasas hacia las 10:16 · lluvia probable (80 %)", RideText.FlyoverWeatherAt(weather.At(7), Zone));
        Assert.Equal("", RideText.FlyoverWeatherAt(FlyoverWeather.None.At(3), Zone));
    }
}
