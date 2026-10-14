using System.Net;
using System.Text;

namespace VientoHud.Core.Tests.Support;

/// <summary>A request as the handler received it.</summary>
internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);

/// <summary>
/// Answers each request with the response <c>respond</c> picks from the whole request (method, URI and body),
/// and remembers the requests it received.
/// </summary>
internal sealed class ScriptedHttpMessageHandler(Func<RecordedRequest, (HttpStatusCode Status, string Body)> respond)
    : HttpMessageHandler
{
    public List<RecordedRequest> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var recorded = new RecordedRequest(request.Method, request.RequestUri!, body);
        Requests.Add(recorded);
        var (status, responseBody) = respond(recorded);
        return new HttpResponseMessage(status) { Content = new StringContent(responseBody, Encoding.UTF8) };
    }
}
