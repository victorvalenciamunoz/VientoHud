using VientoHud.Core.Routing;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Routing;

public class RouteLegsTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Departure = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);

    // A route through the corners, with a track point every 100 m along each straight side.
    private static Route Track(params Coordinate[] corners)
    {
        var points = new List<RoutePoint> { new(corners[0]) };
        foreach (var (from, to) in corners.Zip(corners.Skip(1)))
        {
            var km = from.DistanceKmTo(to);
            var steps = Math.Max(1, (int)Math.Round(km / 0.1));
            var bearing = from.BearingTo(to);
            for (var s = 1; s < steps; s++)
            {
                points.Add(new RoutePoint(from.Destination(bearing, km * s / steps)));
            }
            points.Add(new RoutePoint(to));
        }
        var length = points.Zip(points.Skip(1), (a, b) => a.Position.DistanceKmTo(b.Position)).Sum();
        return new Route(points, [], length, null, "Test");
    }

    private static double AngleBetween(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);

    [Fact]
    public void Cuts_a_straight_road_into_1_km_legs_with_its_bearing()
    {
        var legs = RouteLegs.Split(Track(Leon, Leon.Destination(0, 10)), Departure);

        Assert.Equal(10, legs.Count);
        Assert.All(legs, leg =>
        {
            Assert.Equal(1, leg.LengthKm, precision: 6);
            Assert.True(AngleBetween(0, leg.BearingDegrees) < 0.1, $"Leg {leg.Index}: {leg.BearingDegrees}°");
            Assert.Equal(1, leg.Straightness, precision: 3);
        });
        Assert.Equal(Enumerable.Range(0, 10), legs.Select(l => l.Index));
    }

    [Fact]
    public void Times_each_leg_from_the_departure_at_the_average_speed()
    {
        var legs = RouteLegs.Split(Track(Leon, Leon.Destination(90, 10)), Departure, averageSpeedKmh: 20);

        // 1 km at 20 km/h is 3 minutes.
        Assert.Equal(Departure, legs[0].StartTime);
        Assert.Equal(Departure.AddMinutes(3), legs[0].EndTime);
        Assert.Equal(Departure.AddMinutes(4.5), legs[1].MiddleTime);
        Assert.Equal(Departure.AddMinutes(30), legs[^1].EndTime);
        Assert.All(legs, leg => Assert.Equal(TimeSpan.Zero, leg.StartTime.Offset));
    }

    [Fact]
    public void Assumes_25_km_h_by_default()
    {
        var legs = RouteLegs.Split(Track(Leon, Leon.Destination(90, 10)), Departure);

        Assert.Equal(Departure.AddMinutes(24), legs[^1].EndTime);
    }

    [Theory]
    [InlineData(10.4, 10)]
    [InlineData(10.6, 11)]
    [InlineData(0.3, 1)]
    public void Shares_the_odd_bit_among_equal_legs(double km, int expectedLegs)
    {
        var legs = RouteLegs.Split(Track(Leon, Leon.Destination(45, km)), Departure);

        Assert.Equal(expectedLegs, legs.Count);
        Assert.All(legs, leg => Assert.Equal(km / expectedLegs, leg.LengthKm, precision: 6));
    }

    [Fact]
    public void Follows_the_turns_of_a_loop()
    {
        // A 3 km square ridden clockwise on the map: east, south, west, north.
        var ne = Leon.Destination(90, 3);
        var se = ne.Destination(180, 3);
        var sw = se.Destination(270, 3);
        var legs = RouteLegs.Split(Track(Leon, ne, se, sw, Leon), Departure);

        Assert.Equal(12, legs.Count);
        double[] sides = [90, 180, 270, 0];
        for (var side = 0; side < 4; side++)
        {
            foreach (var leg in legs.Skip(side * 3).Take(3))
            {
                Assert.True(AngleBetween(sides[side], leg.BearingDegrees) < 0.5, $"Leg {leg.Index}: {leg.BearingDegrees}°");
            }
        }
        Assert.Equal(Leon, legs[^1].End);
    }

    [Fact]
    public void Marks_a_leg_that_turns_back_on_itself_as_not_straight()
    {
        // 0.5 km out and the same 0.5 km back: one leg that ends where it began.
        var legs = RouteLegs.Split(Track(Leon, Leon.Destination(0, 0.5), Leon), Departure);

        var leg = Assert.Single(legs);
        Assert.True(leg.Straightness < 0.01, $"Straightness {leg.Straightness}");
    }

    [Fact]
    public void Crosses_the_180_meridian()
    {
        var west = new Coordinate(0, 179.95);
        var legs = RouteLegs.Split(Track(west, west.Destination(90, 11)), Departure);

        Assert.Equal(11, legs.Count);
        Assert.All(legs, leg => Assert.True(AngleBetween(90, leg.BearingDegrees) < 0.1, $"Leg {leg.Index}: {leg.BearingDegrees}°"));
        Assert.True(legs[^1].End.Longitude < -179.9);
    }

    [Fact]
    public void Joins_the_legs_end_to_end_along_a_recorded_ride()
    {
        var ride = RecordedRides.BoadillaLasso();

        var legs = RouteLegs.Split(ride, Departure);

        Assert.Equal(ride.Points[0].Position, legs[0].Start);
        Assert.Equal(ride.Points[^1].Position, legs[^1].End);
        foreach (var (a, b) in legs.Zip(legs.Skip(1)))
        {
            Assert.Equal(a.End, b.Start);
            Assert.Equal(a.EndKm, b.StartKm, precision: 9);
            Assert.Equal(a.EndTime, b.StartTime);
        }
        Assert.Equal(ride.DistanceKm, legs[^1].EndKm, precision: 6);
        Assert.InRange(legs.Count, 83, 85);
        // Every leg starts on the track: no corner cut.
        Assert.All(legs, leg => Assert.True(
            ride.Points.Zip(ride.Points.Skip(1)).Min(s => DistanceToSegmentKm(leg.Start, s.First.Position, s.Second.Position)) < 0.001,
            $"Leg {leg.Index} starts off the track"));
    }

    [Fact]
    public void Rides_back_against_the_way_out_on_an_out_and_back()
    {
        var legs = RouteLegs.Split(RecordedRides.LaBanezaOutAndBack(), Departure);

        // Adding up each half's legs as arrows (length × straightness in the leg's bearing) gives where that half
        // takes the rider: the way back has to point the opposite way to the way out.
        var half = legs.Count / 2;
        var outward = Heading(legs.Take(half));
        var back = Heading(legs.Skip(half));
        Assert.True(AngleBetween(outward, back) > 170, $"Out {outward:0}°, back {back:0}°");
    }

    // A road north from León, a point every 100 m: first flatKm on the flat, then climbKm at gradePercent.
    private static Route Hill(double flatKm, double climbKm, double gradePercent)
    {
        var points = Enumerable.Range(0, (int)Math.Round((flatKm + climbKm) * 10) + 1)
            .Select(i => i / 10.0)
            .Select(km => new RoutePoint(Leon.Destination(0, km), 800 + Math.Max(0, km - flatKm) * gradePercent * 10))
            .ToList();
        return new Route(points, [], flatKm + climbKm, null, "Test");
    }

    private static double SpeedKmh(RouteLeg leg) => leg.LengthKm / (leg.EndTime - leg.StartTime).TotalHours;

    [Fact]
    public void Shares_the_time_out_by_slope_and_keeps_the_average_speed()
    {
        // 10 km flat and 10 km at 4 %: uphill 52 % slower than on the flat. 20 km at 25 km/h is still 48 minutes.
        var legs = RouteLegs.Split(Hill(10, 10, 4), Departure);

        Assert.Equal(48, (legs[^1].EndTime - Departure).TotalMinutes, precision: 3);
        Assert.Equal(0.48, SpeedKmh(legs[15]) / SpeedKmh(legs[5]), precision: 3);
        Assert.Equal(38.5, SpeedKmh(legs[5]), precision: 1);
        Assert.Equal(4, legs[15].GradePercent, precision: 6);
        Assert.Equal(40, legs[15].ClimbMeters!.Value, precision: 6);
        Assert.Equal(0, legs[5].ClimbMeters!.Value, precision: 6);
    }

    [Fact]
    public void Goes_faster_downhill()
    {
        var legs = RouteLegs.Split(Hill(10, 10, -2), Departure);

        Assert.Equal(1.26, SpeedKmh(legs[15]) / SpeedKmh(legs[5]), precision: 3);
        Assert.Equal(48, (legs[^1].EndTime - Departure).TotalMinutes, precision: 3);
    }

    [Fact]
    public void Counts_slopes_steeper_than_4_percent_as_4_percent()
    {
        var steep = RouteLegs.Split(Hill(10, 10, 8), Departure);

        Assert.Equal(0.48, SpeedKmh(steep[15]) / SpeedKmh(steep[5]), precision: 3);
        Assert.Equal(8, steep[15].GradePercent, precision: 6);
    }

    [Fact]
    public void Keeps_a_steady_speed_when_some_elevation_is_missing()
    {
        var hill = Hill(10, 10, 4);
        var points = hill.Points.Select((p, i) => i == 50 ? p with { ElevationMeters = null } : p).ToList();

        var legs = RouteLegs.Split(new Route(points, [], hill.DistanceKm, null, "Test"), Departure);

        Assert.All(legs, leg =>
        {
            Assert.Null(leg.ClimbMeters);
            Assert.Equal(0, leg.GradePercent);
            Assert.Equal(25, SpeedKmh(leg), precision: 6);
        });
    }

    [Fact]
    public void Times_a_recorded_ride_with_its_elevations()
    {
        // The Boadilla lasso climbs and drops: legs differ in speed, but the whole ride keeps the average.
        var ride = RecordedRides.BoadillaLasso();

        var legs = RouteLegs.Split(ride, Departure, averageSpeedKmh: 30);

        Assert.Equal(Departure.AddHours(legs[^1].EndKm / 30), legs[^1].EndTime);
        Assert.All(legs, leg => Assert.InRange(SpeedKmh(leg), 0.48 * 25, 1.52 * 40));
        Assert.True(legs.Max(SpeedKmh) - legs.Min(SpeedKmh) > 10);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(61)]
    [InlineData(double.NaN)]
    public void Rejects_implausible_speeds(double speedKmh) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RouteLegs.Split(Track(Leon, Leon.Destination(0, 5)), Departure, speedKmh));

    [Theory]
    [InlineData(0)]
    [InlineData(0.05)]
    [InlineData(11)]
    public void Rejects_implausible_leg_lengths(double legKm) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RouteLegs.Split(Track(Leon, Leon.Destination(0, 5)), Departure, legKm: legKm));

    [Fact]
    public void Rejects_a_departure_not_in_utc() =>
        Assert.Throws<ArgumentException>(() =>
            RouteLegs.Split(Track(Leon, Leon.Destination(0, 5)), new DateTimeOffset(2026, 10, 15, 10, 0, 0, TimeSpan.FromHours(2))));

    [Fact]
    public void Rejects_a_route_with_no_length()
    {
        var still = new Route([new RoutePoint(Leon), new RoutePoint(Leon)], [], 0, null, "Test");

        Assert.Throws<ArgumentException>(() => RouteLegs.Split(still, Departure));
    }

    // Bearing of the sum of the legs' straight lines, on a flat map (east, north).
    private static double Heading(IEnumerable<RouteLeg> legs)
    {
        double east = 0, north = 0;
        foreach (var leg in legs)
        {
            var km = leg.LengthKm * leg.Straightness;
            east += km * Math.Sin(leg.BearingDegrees * Math.PI / 180);
            north += km * Math.Cos(leg.BearingDegrees * Math.PI / 180);
        }
        return (Math.Atan2(east, north) * 180 / Math.PI + 360) % 360;
    }

    // Distance from p to the straight segment a-b, on a local flat map; fine for points a few hundred metres apart.
    private static double DistanceToSegmentKm(Coordinate p, Coordinate a, Coordinate b)
    {
        var kmPerDegree = 6371.0088 * Math.PI / 180;
        var cos = Math.Cos(a.Latitude * Math.PI / 180);
        double X(Coordinate c) => (c.Longitude - a.Longitude) * cos * kmPerDegree;
        double Y(Coordinate c) => (c.Latitude - a.Latitude) * kmPerDegree;
        double bx = X(b), by = Y(b), px = X(p), py = Y(p);
        var lengthSquared = bx * bx + by * by;
        var t = lengthSquared == 0 ? 0 : Math.Clamp((px * bx + py * by) / lengthSquared, 0, 1);
        return Math.Sqrt(Math.Pow(px - t * bx, 2) + Math.Pow(py - t * by, 2));
    }
}
