using System.Net.Sockets;
using System.Text;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Minimal FreeSWITCH Event Socket Library (ESL) client.
/// Handles auth handshake, event subscription, and line-by-line event reading.
/// </summary>
public sealed class EslClient : IAsyncDisposable
{
    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;

    public async Task ConnectAsync(string host, int port, string password, CancellationToken ct)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(host, port, ct);

        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\n",
        };

        // FreeSWITCH sends "Content-Type: auth/request" first
        await ReadMessageAsync(ct);

        await _writer.WriteLineAsync($"auth {password}");
        await _writer.WriteLineAsync();

        var reply = await ReadMessageAsync(ct);
        if (reply?.GetHeader("Reply-Text")?.StartsWith("+OK") != true)
            throw new InvalidOperationException("ESL authentication failed — check EslPassword config.");
    }

    public async Task SubscribeAsync(string eventNames, CancellationToken ct)
    {
        await _writer!.WriteLineAsync($"event plain {eventNames}");
        await _writer.WriteLineAsync();
        await ReadMessageAsync(ct); // consumes the +OK reply
    }

    /// <summary>Reads one ESL message (headers + optional body). Returns null on clean disconnect.</summary>
    public async Task<EslMessage?> ReadMessageAsync(CancellationToken ct)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? line;
        while (!string.IsNullOrEmpty(line = await _reader!.ReadLineAsync(ct)))
        {
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (headers.Count == 0) return null;

        string? body = null;
        if (headers.TryGetValue("Content-Length", out var lenStr) && int.TryParse(lenStr, out var len) && len > 0)
        {
            var buf = new char[len];
            var read = 0;
            while (read < len)
                read += await _reader.ReadAsync(buf.AsMemory(read, len - read), ct);
            body = new string(buf);
        }

        return new EslMessage(headers, body);
    }

    public ValueTask DisposeAsync()
    {
        _writer?.Dispose();
        _reader?.Dispose();
        _tcp?.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>One message received from the FreeSWITCH ESL socket.</summary>
public sealed class EslMessage(Dictionary<string, string> headers, string? body)
{
    public string? ContentType => GetHeader("Content-Type");

    public string? GetHeader(string key) =>
        headers.TryGetValue(key, out var v) ? v : null;

    /// <summary>
    /// Parses the plain-text event body into a key-value dictionary.
    /// FreeSWITCH URL-encodes values in plain events (e.g. %2B → +).
    /// </summary>
    public Dictionary<string, string> ParseBody()
    {
        if (body is null) return [];
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = line.IndexOf(':');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var val = Uri.UnescapeDataString(line[(idx + 1)..].Trim());
            result[key] = val;
        }
        return result;
    }
}
