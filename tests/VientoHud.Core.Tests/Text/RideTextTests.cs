using VientoHud.Core.Tests.Support;
using VientoHud.Core.Routing;
using VientoHud.Core.Text;

namespace VientoHud.Core.Tests.Text;

public class RideTextTests
{
    private static readonly Coordinate Origin = TestPlaces.Origin;
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 8, 30, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    // A ride of a 2-point route with the score given; enough for the sentences.
    private static RideOption Ride(double firstHalf = 5, double secondHalf = -3, bool gustsKnown = true, params GustyStretch[] gusty) =>
        new(0, new Route([new RoutePoint(Origin), new RoutePoint(Origin.Destination(90, 1))], [], 1, null, "Test"), false, [],
            new RideScore(firstHalf, secondHalf, gusty, gustsKnown));

    private static DepartureAdvice Advice(bool wait, double rainSaved = 0, double gustsSaved = 0)
    {
        var now = new DepartureOption(Now, [Ride()]);
        var later = new DepartureOption(Now.AddMinutes(30), [Ride()]);
        return wait ? new DepartureAdvice(later, now, [now, later], rainSaved, gustsSaved) : new DepartureAdvice(now, now, [now, later], 0, 0);
    }

    [Fact]
    public void Says_to_leave_now_or_to_wait_and_why()
    {
        Assert.Equal("Sal ahora (10:30).", RideText.Recommendation(Advice(wait: false), fromNow: true, Zone));
        Assert.Equal("Sal a las 10:30 (10:30).", RideText.Recommendation(Advice(wait: false), fromNow: false, Zone));
        Assert.Equal(
            "Mejor espera: sal a las 11:00 en vez de ahora; evitas 3 km con rachas de lado peligrosas y te ahorras 5,5 km de lluvia probable.",
            RideText.Recommendation(Advice(wait: true, rainSaved: 5.5, gustsSaved: 3), fromNow: true, Zone));
        Assert.Equal(
            "Mejor espera: sal a las 11:00 en vez de a las 10:30; te ahorras 5 km de lluvia probable.",
            RideText.Recommendation(Advice(wait: true, rainSaved: 5), fromNow: false, Zone));
        var none = new DepartureOption(Now, []);
        Assert.Equal("No hay ninguna vuelta válida.", RideText.Recommendation(new DepartureAdvice(none, none, [none], 0, 0), true, Zone));
    }

    [Fact]
    public void The_app_headline_is_short_and_the_why_goes_below()
    {
        Assert.Equal("Sal ahora (10:30).", RideText.Headline(Advice(wait: false), fromNow: true, Zone));
        Assert.Null(RideText.WaitReason(Advice(wait: false), fromNow: true, Zone));
        Assert.Equal("Espera: sal a las 11:00.", RideText.Headline(Advice(wait: true, rainSaved: 32.1), fromNow: true, Zone));
        Assert.Equal("En vez de ahora, te ahorras 32,1 km de lluvia probable",
            RideText.WaitReason(Advice(wait: true, rainSaved: 32.1), fromNow: true, Zone));
        Assert.Equal("En vez de las 10:30, evitas 3 km con rachas de lado y te ahorras 5 km de lluvia probable",
            RideText.WaitReason(Advice(wait: true, rainSaved: 5, gustsSaved: 3), fromNow: false, Zone));
    }

    [Fact]
    public void Headlines_where_to_head()
    {
        Assert.Equal("Hacia el SO", RideText.DirectionHeadline(new Core.Wind.StartDirectionAdvice(Core.Wind.StartAdviceKind.HeadIntoWind, 225, 225, 18, 18, 1, 3)));
        Assert.Equal("Da igual hacia dónde", RideText.DirectionHeadline(new Core.Wind.StartDirectionAdvice(Core.Wind.StartAdviceKind.Calm, null, 90, 2, 3, 0.7, 3)));
        Assert.Equal("Sin rumbo claro", RideText.DirectionHeadline(new Core.Wind.StartDirectionAdvice(Core.Wind.StartAdviceKind.Variable, null, 90, 5, 15, 0.3, 3)));
    }

    [Fact]
    public void Describes_the_wind_advantage_as_it_feels()
    {
        Assert.Equal(
            "ventaja de viento +8 km/h (de cara 5 km/h en la 1.ª mitad, a favor 3 km/h en la 2.ª)",
            RideText.Advantage(Ride().Score));
        Assert.Equal("sin viento de cara ni a favor", RideText.Along(0.4));
    }

    [Fact]
    public void Warns_of_gusts_across_only_when_there_are_or_they_are_unknown()
    {
        Assert.Null(RideText.Gusts(Ride().Score));
        Assert.Equal(
            "Sin previsión de rachas para alguna hora: no se puede avisar de las de lado.",
            RideText.Gusts(Ride(gustsKnown: false).Score));
        Assert.Equal(
            "¡Ojo! Rachas de lado de más de 40 km/h: km 24-26 (hasta 45 km/h).",
            RideText.Gusts(Ride(gusty: new GustyStretch(24, 26, 45.2)).Score));
    }

    [Fact]
    public void Spanish_numbers_and_compass_points()
    {
        Assert.Equal("12,3 km", SpanishText.Km(12.34));
        Assert.Equal("km 7,5", SpanishText.KmAt(7.5));
        Assert.Equal("18 km/h", SpanishText.Kmh(18.4));
        Assert.Equal("34 %", SpanishText.Percent(0.34));
        Assert.Equal("-3 km/h", SpanishText.Signed(-3.2));
        Assert.Equal("SO", SpanishText.Point(225));
        Assert.Equal("hacia el O (270°)", SpanishText.Heading(270));
        Assert.Equal("Hacia el O", SpanishText.Capitalized("hacia el O"));
    }

    [Fact]
    public void Names_the_gpx_and_its_file_as_hammerhead_shows_it()
    {
        Assert.Equal("Sal 10:30 · 60 km SO", RideGpx.Name(Now, 60.4, 225, RouteShape.Loop, Zone));
        Assert.Equal("Sal 10:30 · 60 km SO ida y vuelta", RideGpx.Name(Now, 60.4, 225, RouteShape.OutAndBack, Zone));
        Assert.Equal("1-vuelta-60km-SO-20261019-1030.gpx", RideGpx.FileName(RouteShape.Loop, 60, 225, Now, Zone, rank: 1));
        Assert.Equal("ida-y-vuelta-56km-NO-20261019-1030.gpx", RideGpx.FileName(RouteShape.OutAndBack, 55.6, 315, Now, Zone));
        Assert.Equal(
            "vuelta-60km-N-20261019-1030-descartada.gpx",
            RideGpx.FileName(RouteShape.Loop, 60, 0, Now, Zone, discarded: true));
    }

    [Fact]
    public void Alerts_become_short_waypoints_with_the_details_in_the_description()
    {
        var gust = RideGpx.Waypoint(new RideAlert(RideAlertKind.Gusts, Origin, 24, 26.4, Now.AddHours(1), 45.2), Zone);
        var rain = RideGpx.Waypoint(new RideAlert(RideAlertKind.Rain, Origin, 9, 41, Now.AddMinutes(52), 0.75), Zone);
        var area = RideGpx.Waypoint(new RideAlert(RideAlertKind.AreaShowers, Origin, 75, 75, Now.AddHours(3), 0.25), Zone);

        Assert.Equal("Racha lado 45", gust.Name);
        Assert.Equal("km 24-26: rachas de lado de hasta 45 km/h hacia las 11:30.", gust.Description);
        Assert.Equal("Lluvia ~11:22", rain.Name);
        Assert.Equal("km 9-41: lluvia probable (75 %) hacia las 11:22.", rain.Description);
        Assert.Equal("Posibles chubascos", area.Name);
        Assert.Equal("Desde el km 75 (hacia las 13:30): posibles chubascos en la zona (hasta 25 %).", area.Description);
    }
}
