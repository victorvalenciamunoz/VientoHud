using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;

namespace VientoHud.Core.Tests.Wind;

public class ObservedWindTests
{
    private const double KmhPerKnot = 1.852;
    private static readonly DateTimeOffset October1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly WeatherStation Leln = new("LELN", "León", new Coordinate(42.5889, -5.6556), 916, 0);
    private static readonly WeatherStation Lemd = new("LEMD", "Madrid-Barajas", new Coordinate(40.4719, -3.5626), 609, 0);

    // Real METAR of LEMD and LELN, 1-2 October 2026 (the fixture of phase 1).
    private static async Task<IReadOnlyList<WindObservation>> RecordedAsync()
    {
        var handler = new StubHttpMessageHandler(_ => (HttpStatusCode.OK, Fixture.Read("Iem/asos-LEMD-LELN-2026-10-01-to-03.csv")));
        return await new IemWindObservationSource(new HttpClient(handler))
            .GetObservationsAsync(["LEMD", "LELN"], October1, October1.AddDays(2));
    }

    private static WindObservation At(int hour, int minute, double kmh, double? from) =>
        new("TEST", October1.AddHours(hour).AddMinutes(minute), kmh, null, from);

    [Fact]
    public async Task Takes_the_report_on_the_hour_of_each_hour()
    {
        var hours = ObservedWind.Hourly("LELN", await RecordedAsync(), October1.AddHours(9), 4)!;

        // LELN 09:00 04005KT, 10:00 08006KT, 11:00 05004KT, 12:00 05005KT.
        Assert.Equal(
            [
                new WindSample(October1.AddHours(9), 5 * KmhPerKnot, null, 40, "METAR LELN"),
                new WindSample(October1.AddHours(10), 6 * KmhPerKnot, null, 80, "METAR LELN"),
                new WindSample(October1.AddHours(11), 4 * KmhPerKnot, null, 50, "METAR LELN"),
                new WindSample(October1.AddHours(12), 5 * KmhPerKnot, null, 50, "METAR LELN"),
            ],
            hours);
    }

    [Fact]
    public async Task Gives_a_variable_wind_the_direction_of_the_nearest_report_with_one()
    {
        // LELN 08:00 VRB02KT; the closest report with a direction is 08:30, 02004KT.
        var hours = ObservedWind.Hourly("LELN", await RecordedAsync(), October1.AddHours(8), 1)!;

        Assert.Equal(new WindSample(October1.AddHours(8), 2 * KmhPerKnot, null, 20, "METAR LELN"), Assert.Single(hours));
    }

    [Fact]
    public async Task Gives_nothing_when_some_hour_has_no_report()
    {
        // The recording ends on 3 October at 00:00.
        Assert.Null(ObservedWind.Hourly("LELN", await RecordedAsync(), October1.AddHours(46), 4));
    }

    [Fact]
    public void Takes_the_closest_report_within_15_minutes_and_the_earlier_on_a_tie()
    {
        WindObservation[] reports = [At(9, 50, 10, 90), At(10, 10, 20, 180), At(11, 5, 30, 270)];

        var hours = ObservedWind.Hourly("TEST", reports, October1.AddHours(10), 2)!;

        Assert.Equal([10.0, 30], hours.Select(h => h.SpeedKmh));
        Assert.Null(ObservedWind.Hourly("TEST", [At(10, 20, 10, 90)], October1.AddHours(10), 1));
    }

    [Fact]
    public void Calls_a_calm_with_no_report_with_a_direction_north()
    {
        var hours = ObservedWind.Hourly("TEST", [At(10, 0, 0, null)], October1.AddHours(10), 1)!;

        Assert.Equal(0, Assert.Single(hours).DirectionFromDegrees);
    }

    [Fact]
    public async Task Builds_a_field_from_the_stations_that_have_every_hour()
    {
        var observations = await RecordedAsync();
        var lost = Leln with { Id = "LEXX" };

        var field = ObservedWind.Field([Lemd, Leln, lost], observations, October1.AddHours(9), 3)!;

        Assert.Equal([Lemd.Position, Leln.Position], field.Points.Select(p => p.Position));
        Assert.Null(ObservedWind.Field([lost], observations, October1.AddHours(9), 3));
    }
}
