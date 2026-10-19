using System.Net;
using VientoHud.Core.Net;
using VientoHud.Core.Tests.Support;

namespace VientoHud.Core.Tests.Net;

public class ResilientHttpTests
{
    private static readonly Uri Anywhere = new("https://example.test/data");

    // Answers the statuses given, in order, then 200 "ok"; no wait between retries.
    private static (HttpClient Client, ScriptedHttpMessageHandler Handler) Client(params HttpStatusCode[] first)
    {
        var calls = 0;
        var handler = new ScriptedHttpMessageHandler(_ =>
            calls < first.Length ? (first[calls++], "fail") : (HttpStatusCode.OK, "ok"));
        return (new HttpClient(ResilientHttp.CreateHandler(handler, TimeSpan.Zero)), handler);
    }

    [Fact]
    public async Task A_passing_failure_is_retried_and_the_caller_never_sees_it()
    {
        var (client, handler) = Client(HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway);

        var response = await client.GetAsync(Anywhere);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task A_usage_limit_is_not_retried()
    {
        // Open-Meteo's 429 say "try again in one minute" or "tomorrow": seconds later it would be the same.
        var (client, handler) = Client(HttpStatusCode.TooManyRequests);

        var response = await client.GetAsync(Anywhere);

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task After_three_retries_the_failure_is_returned_as_it_came()
    {
        var (client, handler) = Client(
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable,
            HttpStatusCode.ServiceUnavailable, HttpStatusCode.ServiceUnavailable);

        var response = await client.GetAsync(Anywhere);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public async Task A_refusal_is_not_retried()
    {
        // brouter.de blocks with 403 (DECISIONS 018): asking again would make it worse.
        var (client, handler) = Client(HttpStatusCode.Forbidden);

        var response = await client.GetAsync(Anywhere);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void The_client_carries_the_user_agent_and_the_timeout()
    {
        using var client = ResilientHttp.CreateClient(new ScriptedHttpMessageHandler(_ => (HttpStatusCode.OK, "")));

        Assert.Equal(ResilientHttp.DefaultTimeout, client.Timeout);
        Assert.Equal("VientoHud/0.1", client.DefaultRequestHeaders.UserAgent.ToString());
    }
}
