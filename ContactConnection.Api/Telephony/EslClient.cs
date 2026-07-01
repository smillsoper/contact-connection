using System.Net.Sockets;
using System.Text;
using ContactConnection.Application.Interfaces.Services;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Minimal FreeSWITCH Event Socket Library (ESL) client.
/// Handles auth handshake, event subscription, and line-by-line event reading.
/// Also implements IEslCommander so it can be injected into telephony node handlers.
/// </summary>
public sealed class EslClient : IAsyncDisposable, IEslCommander
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

    // ── Command sending ──────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a FreeSWITCH API command and waits for the api/response reply.
    /// Safe to call from within an event handler — the main read loop is awaiting this handler,
    /// so there is no concurrent ReadMessageAsync on the socket.
    /// </summary>
    public async Task SendApiAsync(string command, CancellationToken ct = default)
    {
        await _writer!.WriteLineAsync($"api {command}");
        await _writer.WriteLineAsync();
        await ReadMessageAsync(ct);  // consume the api/response
    }

    public Task KillChannelAsync(string uuid, int causeCode, CancellationToken ct = default) =>
        SendApiAsync($"uuid_kill {uuid} Q.850:{causeCode}", ct);

    public Task AnswerChannelAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_answer {uuid}", ct);

    public Task HangupChannelAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_hangup {uuid} NORMAL_CLEARING", ct);

    // Transfer the parked inbound channel to the agent using FreeSWITCH's inline dialplan.
    // uuid_transfer moves the parked channel into a bridge application that FreeSWITCH
    // handles internally — it originates a new SIP call to the agent and bridges them.
    // This is more reliable than bgapi originate &bridge(uuid), which passes the UUID
    // as a dial string (invalid) and causes an immediate BYE after the agent answers.
    public async Task BridgeToAgentAsync(string uuid, string extension, string domain, string callerNumber, CancellationToken ct = default)
    {
        // Set effective caller ID before bridging so the SIP INVITE to the agent shows the original ANI
        await SetChannelVarAsync(uuid, "effective_caller_id_number", callerNumber, ct);
        await SetChannelVarAsync(uuid, "effective_caller_id_name", callerNumber, ct);
        await SendApiAsync($"uuid_transfer {uuid} 'bridge:user/{extension}@{domain}' inline", ct);
    }

    public Task SetChannelVarAsync(string uuid, string name, string value, CancellationToken ct = default) =>
        SendApiAsync($"uuid_setvar {uuid} {name} {value}", ct);

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
