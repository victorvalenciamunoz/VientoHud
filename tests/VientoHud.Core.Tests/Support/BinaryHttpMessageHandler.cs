using System.Net;

namespace VientoHud.Core.Tests.Support;

/// <summary>Answers each request with canned bytes and remembers the requests it received.</summary>
internal sealed class BinaryHttpMessageHandler(Func<Uri, (HttpStatusCode Status, byte[] Body)> respond) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request.RequestUri!);
        var (status, body) = respond(request.RequestUri!);
        return Task.FromResult(new HttpResponseMessage(status) { Content = new ByteArrayContent(body) });
    }
}
