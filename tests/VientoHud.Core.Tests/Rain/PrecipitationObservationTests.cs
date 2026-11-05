using System.Net;
using VientoHud.Core.Rain.Observations;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Rain;

public partial class PrecipitationObservationTests
{
    [Theory]
    [InlineData("-RA", true)]
    [InlineData("RA BR", true)]
    [InlineData("+SHRA", true)]
    [InlineData("TS SHRA", true)]
    [InlineData("+TSGRRA", true)]
    [InlineData("-DZRA", true)]
    [InlineData("DZ", true)]
    [InlineData("-SN", true)]
    [InlineData("FZRA", true)]
    [InlineData("", false)]
    [InlineData("BR", false)]
    [InlineData("BCFG", false)]
    [InlineData("TS", false)] // thunder heard, no rain at the station
    [InlineData("VCSH", false)] // showers in the vicinity
    [InlineData("VCTS VCSH", false)]
    [InlineData("RERA", false)] // recent rain, not now
    [InlineData("BLSN", false)] // snow blown from the ground
    [InlineData("TS VCSH", false)]
    public void Reads_precipitation_from_metar_weather(string codes, bool expected)
    {
        Assert.Equal(expected, MetarWeather.HasPrecipitation(codes));
    }
}
