using System.Net;
using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.OpenMeteo;

namespace VientoHud.Core.Tests.Routing;

public class RouteWindTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    private static Route Straight(double bearing, double km)
    {
        var points = Enumerable.Range(0, (int)(km * 10) + 1)
            .Select(i => new RoutePoint(Leon.Destination(bearing, i / 10.0)))
            .ToList();
        return new Route(points, [], km, null, "Test");
    }

    // A 1 km leg ridden towards bearing, with that straightness.
    private static RouteLeg Leg(double bearing, double straightness = 1) =>
        new(0, 0, 1, Leon, Leon.Destination(bearing, straightness), Leon, bearing, straightness, null, Eight, Eight.AddMinutes(2.4));

    private static LegWind Meet(double bearing, double speed, double from, double? gust = null, double straightness = 1) =>
        new(Leg(bearing, straightness), new LocalWind(speed, gust, from, "model"));

    [Theory]
    [InlineData(0, 0, 20, 0)] // going north, wind from the north: all in the face
    [InlineData(0, 180, -20, 0)] // from the south: all from behind
    [InlineData(0, 90, 0, 20)] // from the east: from the right
    [InlineData(0, 270, 0, -20)] // from the west: from the left
    [InlineData(90, 135, 14.142, 14.142)] // going east, wind from the south-east: half and half, from the right
    [InlineData(225, 45, -20, 0)] // going south-west, wind from the north-east: from behind
    public void Splits_the_wind_into_face_and_side(double bearing, double from, double expectedHead, double expectedCross)
    {
        var meeting = Meet(bearing, 20, from);

        Assert.Equal(expectedHead, meeting.HeadwindKmh, precision: 3);
        Assert.Equal(expectedCross, meeting.CrosswindKmh, precision: 3);
    }

    [Fact]
    public void Scales_the_wind_in_the_face_by_how_straight_the_leg_is()
    {
        var meeting = Meet(0, 20, 0, straightness: 0.5);

        Assert.Equal(10, meeting.HeadwindKmh, precision: 9);
    }

    [Fact]
    public void Measures_the_gust_across_from_either_side()
    {
        Assert.Equal(30, Meet(0, 20, 270, gust: 30).CrossGustKmh!.Value, precision: 9);
        Assert.Equal(30, Meet(0, 20, 90, gust: 30).CrossGustKmh!.Value, precision: 9);
        Assert.Null(Meet(0, 20, 90).CrossGustKmh);
    }

    [Theory]
    [InlineData(10, 2)]
    [InlineData(12.4, 2)]
    [InlineData(13, 3)]
    [InlineData(2, 1)]
    public void Asks_for_the_wind_every_5_km(double km, int expected)
    {
        var points = RouteWind.SamplePoints(Straight(0, km));

        Assert.Equal(expected, points.Count);
        // In the middle of each stretch.
        Assert.Equal(km / expected / 2, Leon.DistanceKmTo(points[0]), precision: 3);
    }

    [Fact]
    public void Asks_for_the_wind_at_19_places_on_a_96_km_ride() =>
        Assert.Equal(19, RouteWind.SamplePoints(RecordedRides.LaBanezaOutAndBack()).Count);

    [Fact]
    public async Task Gives_each_leg_the_wind_at_its_middle_when_it_is_ridden()
    {
        // 10 km north at 20 km/h: 30 minutes. The wind turns from north to east between 08:00 and 09:00.
        var route = Straight(0, 10);
        var legs = RouteLegs.Split(route, Eight, averageSpeedKmh: 20);
        var provider = new FakeWindForecastProvider(
            [new WindSample(Eight, 20, null, 0, "fake"), new WindSample(Eight.AddHours(1), 20, null, 90, "fake")]);

        var winds = await RouteWind.ForecastAsync(provider, route, legs);

        Assert.Equal(2, Assert.Single(provider.Requests).Count);
        Assert.Equal(legs, winds.Select(w => w.Leg));
        // Leg 0 is ridden at 08:01:30, mostly still from the north: in the face. Leg 9 at 08:28:30, from about 42°:
        // 20 × cos 42° ≈ 15.
        Assert.True(winds[0].HeadwindKmh > 19, $"{winds[0].HeadwindKmh}");
        Assert.InRange(winds[9].HeadwindKmh, 14, 16);
    }

    [Fact]
    public async Task Turns_a_south_west_wind_into_face_out_and_back_on_the_la_baneza_ride()
    {
        // Same wind everywhere, from the south-west; the ride goes south-west and comes back.
        var ride = RecordedRides.LaBanezaOutAndBack();
        var legs = RouteLegs.Split(ride, Eight);
        var provider = new FakeWindForecastProvider(
            Enumerable.Range(0, 6).Select(h => new WindSample(Eight.AddHours(h), 20, 30, 225, "fake")).ToList());

        var winds = await RouteWind.ForecastAsync(provider, ride, legs);

        var half = winds.Count / 2;
        Assert.True(winds.Take(half).Average(w => w.HeadwindKmh) > 12);
        Assert.True(winds.Skip(half).Average(w => w.HeadwindKmh) < -12);
    }

    [Fact]
    public async Task Reads_the_real_forecast_for_the_la_baneza_ride()
    {
        // Recorded 2026-10-15: AROME for 2026-10-16 08:00-12:00 UTC at the ride's 19 wind points. A weak south wind
        // that strengthens: on the way back (north-east) it comes from the right.
        var ride = RecordedRides.LaBanezaOutAndBack();
        var legs = RouteLegs.Split(ride, Eight);
        var handler = new StubHttpMessageHandler(
            HttpStatusCode.OK, Fixture.Read("OpenMeteo/labaneza-route-19-points-arome-2026-10-16T08.json"));
        var provider = new OpenMeteoWindForecastProvider(
            new HttpClient(handler), new OpenMeteoOptions { Models = ["meteofrance_arome_france"] });

        var winds = await RouteWind.ForecastAsync(provider, ride, legs);

        Assert.Single(handler.Requests);
        Assert.Equal(96, winds.Count);
        Assert.All(winds, w => Assert.Equal("meteofrance_arome_france", w.Wind.Model));
        Assert.All(winds, w => Assert.InRange(w.Wind.SpeedKmh, 0, 15));
        Assert.True(winds.Skip(48).Average(w => w.CrosswindKmh) > 3);
    }
}
