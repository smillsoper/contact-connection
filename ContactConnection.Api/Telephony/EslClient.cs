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

    // Transfer the parked inbound channel to the agent's registered WebRTC endpoint.
    // Resolves the agent's actual SIP contact via sofia_contact (registration lookup),
    // then bridges using uuid_transfer inline so the parked channel connects to the agent.
    public async Task BridgeToAgentAsync(string uuid, string extension, string domain, string callerNumber, CancellationToken ct = default)
    {
        // Set effective caller ID before bridging so the SIP INVITE to the agent shows the original ANI
        await SetChannelVarAsync(uuid, "effective_caller_id_number", callerNumber, ct);
        await SetChannelVarAsync(uuid, "effective_caller_id_name", callerNumber, ct);

        // Resolve the agent's registered WebRTC contact URI — this is the sofia profile +
        // full SIP contact with fs_path for WebSocket routing, e.g.:
        // sofia/internal/sip:abc@host.invalid;transport=ws;fs_path=sip:abc@172.x.x.x:port;transport=ws
        var contact = await SendApiBodyAsync($"sofia_contact {extension}@{domain}", ct);
        if (string.IsNullOrEmpty(contact) || contact.StartsWith("-ERR"))
            throw new InvalidOperationException(
                $"Agent {extension}@{domain} is not registered in FreeSWITCH. sofia_contact returned: {contact}");

        await SendApiAsync($"uuid_transfer {uuid} 'bridge:{contact}' inline", ct);
    }

    private async Task<string?> SendApiBodyAsync(string command, CancellationToken ct)
    {
        await _writer!.WriteLineAsync($"api {command}");
        await _writer.WriteLineAsync();
        var msg = await ReadMessageAsync(ct);
        return msg?.Body?.Trim();
    }

    public Task SetChannelVarAsync(string uuid, string name, string value, CancellationToken ct = default) =>
        SendApiAsync($"uuid_setvar {uuid} {name} {value}", ct);

    public Task BreakChannelAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_break {uuid} all", ct);

    public Task BroadcastAsync(string uuid, string mediaArg, CancellationToken ct = default) =>
        SendApiAsync($"uuid_broadcast {uuid} {mediaArg} aleg", ct);

    public Task BridgeChannelsAsync(string uuid1, string uuid2, CancellationToken ct = default) =>
        SendApiAsync($"uuid_bridge {uuid1} {uuid2}", ct);

    public Task SendDtmfAsync(string uuid, string digits, int durationMs, CancellationToken ct = default) =>
        SendApiAsync($"uuid_send_dtmf {uuid} {digits}@{durationMs}", ct);

    /// <returns>(uuid, null) on success; (null, errorDetail) on failure so callers can log the cause.</returns>
    public async Task<(string? Uuid, string? Error)> OriginateAndParkAsync(string extension, string domain, string callerNumber, CancellationToken ct = default)
    {
        var contact = await SendApiBodyAsync($"sofia_contact {extension}@{domain}", ct);
        if (string.IsNullOrEmpty(contact) || contact.StartsWith("-ERR"))
            return (null, $"sofia_contact {extension}@{domain} → {contact ?? "(null)"}");

        var vars = $"{{sip_auto_answer=true,sip_h_Alert-Info=answer-after=0," +
                   $"effective_caller_id_number={callerNumber},effective_caller_id_name={callerNumber}," +
                   $"cc_whisper=true}}";
        var response = await SendApiBodyAsync($"originate {vars}{contact} &park()", ct);

        return response?.StartsWith("+OK") == true
            ? (response["+OK".Length..].Trim(), null)
            : (null, $"originate → {response ?? "(null)"}");
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
    public string? Body => body;

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
