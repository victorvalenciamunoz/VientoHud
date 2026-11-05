using System.Net;
using System.Net.Http.Headers;

namespace VientoHud.Core.Net;

/// <summary>
/// A read-only, seekable view of a file on a server that answers byte ranges (HTTP 206), fetched in blocks of
/// <see cref="BlockSize"/> as they are read and kept in <see cref="Blocks"/> (phase 9, step 2b): reading a corner of
/// a radar composite of all of Europe takes a tenth of the file. Reads block the calling thread; use it off the UI.
/// </summary>
public sealed class HttpRangeStream : Stream
{
    public const int DefaultBlockSize = 64 * 1024;

    private readonly HttpClient _http;
    private readonly Uri _uri;
    private long _position;

    private HttpRangeStream(HttpClient http, Uri uri, long length, int blockSize, IDictionary<long, byte[]> blocks)
    {
        _http = http;
        _uri = uri;
        Length = length;
        BlockSize = blockSize;
        Blocks = blocks;
    }

    public int BlockSize { get; }

    /// <summary>The blocks fetched so far, by index; pass the same dictionary to reuse them for the same file.</summary>
    public IDictionary<long, byte[]> Blocks { get; }

    /// <summary>HTTP requests made by this stream, the first one included.</summary>
    public int Requests { get; private set; }

    /// <summary>
    /// Opens the file at <paramref name="uri"/> by fetching its first block (which tells its length); null when the
    /// server says it is not there (404). <paramref name="blocks"/> may already hold blocks of the same file.
    /// </summary>
    /// <exception cref="HttpRequestException">Any other failure, including an error status.</exception>
    public static async Task<HttpRangeStream?> OpenAsync(
        HttpClient http, Uri uri, int blockSize = DefaultBlockSize, IDictionary<long, byte[]>? blocks = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentOutOfRangeException.ThrowIfLessThan(blockSize, 512);
        blocks ??= new Dictionary<long, byte[]>();
        using var request = RangeRequest(uri, 0, blockSize - 1);
        using var response = await http.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        long length;
        if (response.StatusCode == HttpStatusCode.PartialContent && response.Content.Headers.ContentRange?.Length is { } total)
        {
            length = total;
        }
        else
        {
            // The server ignored the range and sent it all: keep it all.
            length = body.Length;
        }
        var stream = new HttpRangeStream(http, uri, length, blockSize, blocks) { Requests = 1 };
        stream.Store(0, body);
        return stream;
    }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length { get; }

    public override long Position
    {
        get => _position;
        set => _position = value is >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        var count = (int)Math.Min(buffer.Length, Math.Max(0, Length - _position));
        var done = 0;
        while (done < count)
        {
            var index = (_position + done) / BlockSize;
            if (!Blocks.TryGetValue(index, out var block))
            {
                Fetch(index, (_position + count - 1) / BlockSize);
                block = Blocks[index];
            }
            var inBlock = (int)((_position + done) % BlockSize);
            var take = Math.Min(count - done, block.Length - inBlock);
            if (take <= 0)
            {
                break;
            }
            block.AsSpan(inBlock, take).CopyTo(buffer[done..]);
            done += take;
        }
        _position += done;
        return done;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            _ => Length + offset,
        };
        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    // Fetches the missing blocks from first to last that follow one another, in one request.
    private void Fetch(long first, long last)
    {
        var end = first;
        while (end < last && !Blocks.ContainsKey(end + 1))
        {
            end++;
        }
        var from = first * BlockSize;
        var to = Math.Min(Length, (end + 1) * BlockSize) - 1;
        using var request = RangeRequest(_uri, from, to);
        // The file reader asks synchronously; this stream is only read off the UI thread.
        using var response = _http.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
        Requests++;
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            Store(first, body);
        }
        else
        {
            // The whole file after all: keep its blocks.
            for (var index = 0L; index * BlockSize < body.Length; index++)
            {
                Blocks[index] = body.AsSpan((int)(index * BlockSize), (int)Math.Min(BlockSize, body.Length - index * BlockSize)).ToArray();
            }
        }
        if (!Blocks.ContainsKey(first))
        {
            throw new HttpRequestException($"The server did not send bytes {from}-{to} of {_uri.AbsolutePath}.");
        }
    }

    private void Store(long firstIndex, byte[] body)
    {
        for (var at = 0; at < body.Length; at += BlockSize)
        {
            Blocks[firstIndex + at / BlockSize] = body.AsSpan(at, Math.Min(BlockSize, body.Length - at)).ToArray();
        }
    }

    private static HttpRequestMessage RangeRequest(Uri uri, long from, long to)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Range = new RangeHeaderValue(from, to);
        return request;
    }
}
