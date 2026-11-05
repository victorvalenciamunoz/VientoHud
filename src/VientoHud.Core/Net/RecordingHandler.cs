using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VientoHud.Core.Net;

/// <summary>
/// Keeps every answer it lets through in a folder and gives it back the next time the same request comes (same
/// method, URL and byte range), without the network (phase 9, step 4: a recorded day lived again costs no quota the
/// second time). Only answers worth keeping: 200, 206 and 404. Not for live use: nothing ever expires.
/// </summary>
public sealed class RecordingHandler(string folder) : DelegatingHandler
{
    /// <summary>Answers given from the folder and from the network, since it was created.</summary>
    public int Replayed { get; private set; }

    public int Recorded { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var key = Key(request);
        var body = Path.Combine(folder, key + ".body");
        var head = Path.Combine(folder, key + ".json");
        if (File.Exists(body) && File.Exists(head))
        {
            using var savedJson = JsonDocument.Parse(await File.ReadAllTextAsync(head, cancellationToken));
            var root = savedJson.RootElement;
            var saved = new Saved(
                root.GetProperty("status").GetInt32(),
                root.TryGetProperty("contentRange", out var cr) ? cr.GetString() : null,
                root.TryGetProperty("contentType", out var ct) ? ct.GetString() : null,
                root.GetProperty("url").GetString()!);
            var content = new ByteArrayContent(await File.ReadAllBytesAsync(body, cancellationToken));
            if (saved.ContentRange is { } range)
            {
                content.Headers.ContentRange = ContentRangeHeaderValue.Parse(range);
            }
            if (saved.ContentType is { } type)
            {
                content.Headers.ContentType = MediaTypeHeaderValue.Parse(type);
            }
            Replayed++;
            return new HttpResponseMessage((HttpStatusCode)saved.Status) { Content = content, RequestMessage = request };
        }

        var response = await base.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.PartialContent or HttpStatusCode.NotFound)
        {
            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Directory.CreateDirectory(folder);
            await File.WriteAllBytesAsync(body, bytes, cancellationToken);
            await File.WriteAllTextAsync(head, Describe(new Saved(
                (int)response.StatusCode,
                response.Content.Headers.ContentRange?.ToString(),
                response.Content.Headers.ContentType?.ToString(),
                request.RequestUri!.ToString())), cancellationToken);
            Recorded++;
            // The content was read: hand on a copy the caller can read again.
            var copy = new ByteArrayContent(bytes);
            foreach (var header in response.Content.Headers)
            {
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            response.Content = copy;
        }
        return response;
    }

    private static string Key(HttpRequestMessage request)
    {
        var text = $"{request.Method} {request.RequestUri} {request.Headers.Range}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..32].ToLowerInvariant();
    }

    // Written by hand: Core does not serialize by reflection (trimmed app).
    private static string Describe(Saved saved)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("status", saved.Status);
            if (saved.ContentRange is { } range)
            {
                json.WriteString("contentRange", range);
            }
            if (saved.ContentType is { } type)
            {
                json.WriteString("contentType", type);
            }
            json.WriteString("url", saved.Url);
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private sealed record Saved(int Status, string? ContentRange, string? ContentType, string Url);
}
