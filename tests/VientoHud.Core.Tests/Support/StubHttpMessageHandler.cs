using System.Net;
using System.Text;

namespace VientoHud.Core.Tests.Support;

/// <summary>Answers each request with a canned response and remembers the requests it received.</summary>
internal sealed class StubHttpMessageHandler(Func<Uri, (HttpStatusCode Status, string Body)> respond) : HttpMessageHandler
{
    public StubHttpMessageHandler(HttpStatusCode status, string body)
        : this(_ => (status, body))
    {
    }

    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var (status, body) = respond(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>Fails every request as if the network were down.</summary>
internal sealed class FailingHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("No such host is known.");
}
