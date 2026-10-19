using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace VientoHud.Core.Net;

/// <summary>
/// The one rule for talking to the services, for the console and the app alike (DECISIONS 049): a passing failure
/// (HTTP 5xx, 408 or a network error) is retried up to three times with growing, slightly random waits (about 2, 4
/// and 8 s), honouring <c>Retry-After</c>. Not a 429: Open-Meteo's say "try again in one minute" or "tomorrow", so
/// asking again in seconds only delays the message. Only the retry: no timeout per attempt (a radar frame on mobile
/// data may take longer than the usual 10 s); the client's own timeout still bounds the whole call, retries included.
/// </summary>
public static class ResilientHttp
{
    public const int DefaultMaxRetries = 3;

    public static readonly TimeSpan DefaultFirstDelay = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public const string UserAgent = "VientoHud/0.1";

    /// <param name="inner">What sends the requests; the platform's default handler when null.</param>
    /// <param name="firstDelay">Wait before the first retry; it doubles each time. Zero in tests.</param>
    /// <param name="maxRetries">Retries after the first attempt.</param>
    public static HttpMessageHandler CreateHandler(
        HttpMessageHandler? inner = null, TimeSpan? firstDelay = null, int maxRetries = DefaultMaxRetries)
    {
        var pipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = maxRetries,
                Delay = firstDelay ?? DefaultFirstDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(
                    HttpClientResiliencePredicates.IsTransient(args.Outcome)
                    && args.Outcome.Result?.StatusCode != System.Net.HttpStatusCode.TooManyRequests),
            })
            .Build();
        return new ResilienceHandler(pipeline) { InnerHandler = inner ?? new HttpClientHandler() };
    }

    /// <summary>A client with the retries, the timeout for the whole call and the app's user agent.</summary>
    public static HttpClient CreateClient(HttpMessageHandler? inner = null)
    {
        var client = new HttpClient(CreateHandler(inner)) { Timeout = DefaultTimeout };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
