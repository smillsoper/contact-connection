using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.FreeSwitchEsl;

/// <summary>
/// Low-level TCP client for the FreeSWITCH Event Socket Library (ESL) protocol.
/// Handles connect, authenticate, event subscription, and sequential event reading.
/// Designed for use inside a single-threaded event loop — not thread-safe for concurrent reads.
/// </summary>
public sealed class FreeSwitchEslClient : IAsyncDisposable
{
    private readonly string _host;
    private readonly int _port;
    private readonly string _password;
    private readonly ILogger _logger;

    private TcpClient? _tcp;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public FreeSwitchEslClient(string host, int port, string password, ILogger logger)
    {
        _host     = host;
        _port     = port;
        _password = password;
        _logger   = logger;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        _tcp = new TcpClient();
        await _tcp.ConnectAsync(_host, _port, ct);

        var stream = _tcp.GetStream();
        _reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
        _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
        {
            AutoFlush = true
        };

        // FreeSWITCH sends auth/request immediately on connect
        var challenge = await ReadBlockAsync(ct);
        if (challenge?.ContentType != "auth/request")
            throw new InvalidOperationException($"Expected auth/request, got: {challenge?.ContentType}");

        await SendRawAsync($"auth {_password}\n\n", ct);

        var authReply = await ReadBlockAsync(ct);
        var replyText = authReply?.Headers.GetValueOrDefault("Reply-Text");
        if (replyText?.StartsWith("+OK") != true)
            throw new InvalidOperationException($"ESL authentication failed: {replyText}");

        _logger.LogInformation("Connected to FreeSWITCH ESL at {Host}:{Port}", _host, _port);
    }

    public async Task SubscribeAsync(IEnumerable<string> events, CancellationToken ct)
    {
        var list = string.Join(" ", events);
        await SendRawAsync($"event plain {list}\n\n", ct);

        var reply = await ReadBlockAsync(ct);
        var replyText = reply?.Headers.GetValueOrDefault("Reply-Text");
        if (replyText?.StartsWith("+OK") != true)
            throw new InvalidOperationException($"ESL event subscribe failed: {replyText}");

        _logger.LogInformation("Subscribed to FreeSWITCH events: {Events}", list);
    }

    /// <summary>
    /// Reads the next message block from the ESL stream.
    /// Returns null only if the connection sends an empty block (peer disconnect).
    /// Plain events have their body key-value pairs merged into Headers.
    /// Non-event messages (command/reply, api/response) are returned with framing headers only.
    /// </summary>
    public Task<EslEvent?> ReadEventAsync(CancellationToken ct) => ReadBlockAsync(ct);

    /// <summary>
    /// Sends a bgapi command (fire-and-forget). The command/reply comes back through the
    /// normal event stream and will be discarded by the caller's content-type filter.
    /// </summary>
    public Task SendBgApiAsync(string command, CancellationToken ct) =>
        SendRawAsync($"bgapi {command}\n\n", ct);

    // ── Internal ─────────────────────────────────────────────────────────────

    private async Task<EslEvent?> ReadBlockAsync(CancellationToken ct)
    {
        if (_reader is null) throw new InvalidOperationException("Not connected.");

        // Read framing header lines until the blank separator
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = await _reader.ReadLineAsync(ct)) is not null)
        {
            if (line.Length == 0) break;
            var colon = line.IndexOf(':');
            if (colon > 0)
                headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        if (headers.Count == 0) return null;

        // Read body if Content-Length is present
        string? rawBody = null;
        if (headers.TryGetValue("Content-Length", out var lenStr)
            && int.TryParse(lenStr, out var len)
            && len > 0)
        {
            var buf   = new char[len];
            var total = 0;
            while (total < len)
                total += await _reader.ReadAsync(buf.AsMemory(total, len - total), ct);
            rawBody = new string(buf);
        }

        // For plain events, parse the body as additional key-value headers and merge them in.
        // Values are URL-encoded by FreeSWITCH when they contain special characters.
        if (headers.GetValueOrDefault("Content-Type") == "text/event-plain" && rawBody is not null)
        {
            foreach (var bodyLine in rawBody.AsSpan().ToString().Split('\n'))
            {
                if (bodyLine.Length == 0) continue;
                var colon = bodyLine.IndexOf(':');
                if (colon <= 0) continue;
                var key   = bodyLine[..colon].Trim();
                var value = Uri.UnescapeDataString(bodyLine[(colon + 1)..].Trim());
                headers[key] = value;
            }
        }

        return new EslEvent(headers, rawBody);
    }

    private async Task SendRawAsync(string text, CancellationToken ct)
    {
        if (_writer is null) throw new InvalidOperationException("Not connected.");
        await _writeLock.WaitAsync(ct);
        try   { await _writer.WriteAsync(text); }
        finally { _writeLock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        _writeLock.Dispose();
        if (_writer is not null) await _writer.DisposeAsync();
        _reader?.Dispose();
        _tcp?.Dispose();
    }
}
