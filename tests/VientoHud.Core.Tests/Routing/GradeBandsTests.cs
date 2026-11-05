using VientoHud.Core.Routing;

namespace VientoHud.Core.Tests.Routing;

public class GradeBandsTests
{
    [Theory]
    [InlineData(-12, GradeBand.Downhill)]
    [InlineData(-2.1, GradeBand.Downhill)]
    [InlineData(-2, GradeBand.Flat)]
    [InlineData(0, GradeBand.Flat)]
    [InlineData(1.9, GradeBand.Flat)]
    [InlineData(2, GradeBand.Gentle)]
    [InlineData(3.9, GradeBand.Gentle)]
    [InlineData(4, GradeBand.Moderate)]
    [InlineData(6.9, GradeBand.Moderate)]
    [InlineData(7, GradeBand.Steep)]
    [InlineData(9.9, GradeBand.Steep)]
    [InlineData(10, GradeBand.VerySteep)]
    [InlineData(25, GradeBand.VerySteep)]
    public void Puts_each_grade_in_its_band(double gradePercent, GradeBand expected)
    {
        Assert.Equal(expected, GradeBands.Of(gradePercent));
    }

    [Fact]
    public void Every_band_has_a_distinct_colour_and_the_list_goes_from_downhill_to_the_steepest()
    {
        var colours = GradeBands.All.Select(GradeBands.Color).ToList();

        Assert.Equal(6, colours.Distinct().Count());
        Assert.All(colours, c => Assert.Matches("^#[0-9A-F]{6}$", c));
        Assert.Equal(GradeBand.Downhill, GradeBands.All[0]);
        Assert.Equal(GradeBand.VerySteep, GradeBands.All[^1]);
    }

    [Fact]
    public void Rejects_a_grade_that_is_not_a_number()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GradeBands.Of(double.NaN));
    }
}
