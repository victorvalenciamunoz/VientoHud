using System.Net;
using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;
using VientoHud.Core.Wind.Observations;
using VientoHud.Core.Wind.Observations.Iem;

namespace VientoHud.Core.Tests.Wind;

public partial class ObservedWindTests
{
    private const double KmhPerKnot = 1.852;
    private static readonly DateTimeOffset October1 = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private static WindObservation At(int hour, int minute, double kmh, double? from) =>
        new("TEST", October1.AddHours(hour).AddMinutes(minute), kmh, null, from);

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
}
