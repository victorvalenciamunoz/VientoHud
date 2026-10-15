using VientoHud.Core.Tests.Support;
using VientoHud.Core.Wind;

namespace VientoHud.Core.Tests.Wind;

public class WindFieldTests
{
    private static readonly Coordinate Leon = new(42.5987, -5.5671);
    private static readonly DateTimeOffset Eight = new(2026, 10, 16, 8, 0, 0, TimeSpan.Zero);

    // Hourly samples from 08:00: (speed, gust, direction FROM) per hour.
    private static WindFieldPoint Point(Coordinate position, params (double Speed, double? Gust, double From)[] hours) =>
        new(position, hours.Select((h, i) => new WindSample(Eight.AddHours(i), h.Speed, h.Gust, h.From, "model")).ToList());

    private static WindFieldPoint Steady(Coordinate position, double speed, double from, string model = "model") =>
        new(position, [new WindSample(Eight, speed, null, from, model), new WindSample(Eight.AddHours(1), speed, null, from, model)]);

    private static double AngleBetween(double a, double b) => Math.Abs((a - b + 540) % 360 - 180);

    [Fact]
    public void Gives_the_sample_itself_at_its_place_and_hour()
    {
        var field = new WindField([Point(Leon, (12, 20, 270), (14, 25, 280))]);

        Assert.Equal(new LocalWind(14, 25, 280, "model"), field.At(Leon, Eight.AddHours(1)));
    }

    [Fact]
    public void Blends_the_two_hours_around_the_moment()
    {
        // 350° and 10° are 20° apart across north: the blend is near north (nearer 10°, the stronger), not south.
        var field = new WindField([Point(Leon, (10, 20, 350), (20, 30, 10))]);

        var wind = field.At(Leon, Eight.AddMinutes(30));

        Assert.Equal(15, wind.SpeedKmh, precision: 9);
        Assert.True(AngleBetween(3.4, wind.DirectionFromDegrees) < 0.1, $"{wind.DirectionFromDegrees}°");
    }

    [Fact]
    public void Takes_the_gust_of_the_hour_the_moment_falls_in()
    {
        // Open-Meteo's gust at 09:00 is the strongest from 08:00 to 09:00.
        var field = new WindField([Point(Leon, (10, 20, 270), (10, 35, 270))]);

        Assert.Equal(35, field.At(Leon, Eight.AddMinutes(10)).GustKmh);
        Assert.Equal(20, field.At(Leon, Eight).GustKmh);
    }

    [Fact]
    public void Blends_two_places_without_turning_a_wind_into_a_calm()
    {
        var north = Leon.Destination(0, 5);
        var field = new WindField([Steady(Leon, 10, 0), Steady(north, 20, 90)]);

        var wind = field.At(Leon.Destination(0, 2.5), Eight);

        Assert.Equal(15, wind.SpeedKmh, precision: 6);
        // Arrows of 10 from the north and 20 from the east: about 63° (atan 20/10).
        Assert.Equal(63.4, wind.DirectionFromDegrees, precision: 0);
    }

    [Fact]
    public void Weighs_the_nearer_place_more()
    {
        var far = Leon.Destination(90, 4);
        var field = new WindField([Steady(Leon, 10, 270), Steady(far, 20, 270)]);

        // 1 km from Leon and 3 km from the other: weights 1 and 1/9.
        var wind = field.At(Leon.Destination(90, 1), Eight);

        Assert.Equal((10 + 20 / 9.0) / (1 + 1 / 9.0), wind.SpeedKmh, precision: 3);
    }

    [Fact]
    public void Uses_only_the_two_nearest_places()
    {
        var field = new WindField(
        [
            Steady(Leon, 10, 270),
            Steady(Leon.Destination(90, 5), 10, 270),
            Steady(Leon.Destination(90, 10), 50, 90),
        ]);

        var wind = field.At(Leon.Destination(90, 2), Eight);

        Assert.Equal(10, wind.SpeedKmh, precision: 6);
        Assert.Equal(270, wind.DirectionFromDegrees, precision: 6);
    }

    [Fact]
    public void Names_every_model_it_blended()
    {
        var field = new WindField([Steady(Leon, 10, 270, "b"), Steady(Leon.Destination(90, 5), 10, 270, "a")]);

        Assert.Equal("a+b", field.At(Leon.Destination(90, 2), Eight).Model);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(61)]
    public void Has_no_wind_outside_its_hours(int minutes)
    {
        var field = new WindField([Steady(Leon, 10, 270)]);

        Assert.Throws<ArgumentOutOfRangeException>(() => field.At(Leon, Eight.AddMinutes(minutes)));
    }

    [Fact]
    public async Task Asks_the_provider_once_for_all_places_and_the_hours_the_ride_lasts()
    {
        var samples = Enumerable.Range(0, 10).Select(h => new WindSample(Eight.AddHours(h), 10, 15, 270, "fake")).ToList();
        var provider = new FakeWindForecastProvider(samples);
        Coordinate[] places = [Leon, Leon.Destination(0, 5), Leon.Destination(90, 5)];

        var field = await WindField.ForecastAsync(provider, places, Eight.AddMinutes(20), Eight.AddHours(3).AddMinutes(49));

        Assert.Equal(places, Assert.Single(provider.Requests));
        Assert.Equal(places, field.Points.Select(p => p.Position));
        // 08:00 to 12:00, so a moment after 11:00 can still be blended.
        Assert.All(field.Points, p => Assert.Equal(Enumerable.Range(0, 5).Select(h => Eight.AddHours(h)), p.Hours.Select(s => s.Time)));
    }

    [Fact]
    public void Rejects_a_place_with_hours_missing_or_out_of_order()
    {
        Assert.Throws<ArgumentException>(() => new WindFieldPoint(Leon, []));
        Assert.Throws<ArgumentException>(() => new WindFieldPoint(Leon,
            [new WindSample(Eight, 10, null, 0, "m"), new WindSample(Eight.AddHours(2), 10, null, 0, "m")]));
        Assert.Throws<ArgumentException>(() => new WindField([]));
    }
}
