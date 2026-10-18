using VientoHud.Core.Rain;
using VientoHud.Core.Routing;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Routing;

public class RideAlertsTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // A ride of 1 km legs going north from León, 2.4 min each; chance of rain per leg (null: beyond 3 h).
    private static RideOption Ride(double?[] rain, IReadOnlyList<GustyStretch>? gusts = null, AreaRain? warning = null)
    {
        var legs = rain.Select((_, i) => new RouteLeg(
            i, i, 1, Leon.Destination(0, i), Leon.Destination(0, i + 1), Leon.Destination(0, i + 0.5), 0, 1, null,
            Eight.AddMinutes(2.4 * i), Eight.AddMinutes(2.4 * (i + 1)))).ToList();
        var winds = legs.Select(l => new LegWind(l, new LocalWind(10, null, 0, "fake"))).ToList();
        var legRain = legs.Select((l, i) => new LegRain(l, rain[i] is { } p
            ? new PlaceRain(p, RainSourceKind.Radar)
            : new PlaceRain(null, RainSourceKind.BeyondHorizon))).ToList();
        return new RideOption(0, null!, false, winds, new RideScore(0, 0, gusts ?? [], true), new RideRain(legRain, warning));
    }

    [Fact]
    public void Marks_where_each_stretch_of_likely_rain_begins()
    {
        var option = Ride([0, 0.6, 0.9, 0.7, 0.1, 0, 0.8, 0]);

        var alerts = RideAlerts.For(option);

        Assert.Equal(2, alerts.Count);
        Assert.Equal((RideAlertKind.Rain, 1.0, 4.0, 0.9), (alerts[0].Kind, alerts[0].StartKm, alerts[0].EndKm, alerts[0].Value));
        Assert.Equal(Leon.Destination(0, 1), alerts[0].Position);
        Assert.Equal(Eight.AddMinutes(2.4), alerts[0].Time);
        Assert.Equal((6.0, 7.0), (alerts[1].StartKm, alerts[1].EndKm));
    }

    [Fact]
    public void Marks_gusts_and_where_the_area_warning_begins_in_riding_order()
    {
        var option = Ride([0.9, 0, 0, null, null], [new GustyStretch(1, 3, 48)],
            new AreaRain(Eight.AddMinutes(8), 0.3, RainSourceKind.Model));

        var alerts = RideAlerts.For(option);

        Assert.Equal([RideAlertKind.Rain, RideAlertKind.Gusts, RideAlertKind.AreaShowers], alerts.Select(a => a.Kind));
        Assert.Equal(48, alerts[1].Value);
        Assert.Equal(3, alerts[2].StartKm);
        Assert.Equal(0.3, alerts[2].Value);
    }

    [Fact]
    public void Possible_rain_and_no_warning_mark_nothing()
    {
        Assert.Empty(RideAlerts.For(Ride([0.3, 0.4, 0.2, null])));
        Assert.Empty(RideAlerts.For(Ride([0.9]) with { Rain = null }));
    }

    [Fact]
    public void Keeps_gusts_first_and_the_longest_rain_when_there_are_too_many()
    {
        // Rain on every other leg (5 one-km stretches) and a 3 km stretch; two gusty stretches; at most 4 points.
        var option = Ride([0.9, 0, 0.9, 0, 0.9, 0, 0.9, 0, 0.9, 0, 0.9, 0.9, 0.9, 0],
            [new GustyStretch(1, 2, 45), new GustyStretch(7, 8, 50)]);

        var alerts = RideAlerts.For(option, maxCount: 4);

        Assert.Equal(4, alerts.Count);
        Assert.Equal(2, alerts.Count(a => a.Kind == RideAlertKind.Gusts));
        Assert.Contains(alerts, a => a.Kind == RideAlertKind.Rain && a.EndKm - a.StartKm == 3);
        Assert.Equal(alerts.OrderBy(a => a.StartKm).Select(a => a.StartKm), alerts.Select(a => a.StartKm));
    }
}
