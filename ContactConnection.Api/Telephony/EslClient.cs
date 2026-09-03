using System.Net.Sockets;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Api.Telephony;

/// <summary>
/// Minimal FreeSWITCH Event Socket Library (ESL) client.
/// Handles auth handshake, event subscription, and line-by-line event reading.
/// Also implements IEslCommander so it can be injected into telephony node handlers.
/// </summary>
public sealed class EslClient(ILogger<EslClient>? logger = null) : IOwnedEslCommander
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
        var reply = await ReadMessageAsync(ct);

        // "api" replies carry the result in the body, not a header — "-ERR ..." on failure.
        // Previously discarded unconditionally, which let a wrong command name (uuid_hangup,
        // not registered in this FreeSWITCH build — only uuid_kill is) fail completely silently.
        if (reply?.Body?.StartsWith("-ERR", StringComparison.Ordinal) == true)
            logger?.LogWarning("ESL api command failed: '{Command}' → {Response}", command, reply.Body.Trim());
    }

    /// <summary>
    /// Fire an api command in the background — returns as soon as FreeSWITCH accepts the job
    /// (its "+OK Job-UUID" command/reply), NOT when the command completes. Use for anything that
    /// would otherwise block the caller for the length of a call, e.g. an <c>originate</c> to an
    /// external number that may ring for the full originate_timeout. The eventual outcome arrives
    /// as normal channel events (CHANNEL_PARK on answer, CHANNEL_HANGUP on no-answer/reject).
    /// </summary>
    public async Task SendBgApiAsync(string command, CancellationToken ct = default)
    {
        await _writer!.WriteLineAsync($"bgapi {command}");
        await _writer.WriteLineAsync();
        var reply = await ReadMessageAsync(ct);
        if (reply?.Body?.StartsWith("-ERR", StringComparison.Ordinal) == true)
            logger?.LogWarning("ESL bgapi command failed: '{Command}' → {Response}", command, reply.Body.Trim());
    }

    public Task KillChannelAsync(string uuid, int causeCode, CancellationToken ct = default) =>
        SendApiAsync($"uuid_kill {uuid} Q.850:{causeCode}", ct);

    public Task AnswerChannelAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_answer {uuid}", ct);

    /// <summary>
    /// uuid_kill, not uuid_hangup — uuid_hangup is not a registered mod_commands API in this
    /// FreeSWITCH build ("Command not found!"), which silently left channels connected.
    /// </summary>
    public Task HangupChannelAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_kill {uuid} NORMAL_CLEARING", ct);

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
        var contact = await ResolveAgentContactAsync(extension, domain, ct);
        // sofia_contact returns "error/user_not_registered" (not "-ERR", not empty) when the
        // agent has no live registration — forwarding that verbatim to bridge: yields a bogus
        // "error/" endpoint and FreeSWITCH drops the caller with CHAN_NOT_IMPLEMENTED. Treat any
        // "error/…" contact, empty, or "-ERR" as "not reachable" so callers can fall back.
        if (!IsResolvedContact(contact))
            throw new InvalidOperationException(
                $"Agent {extension}@{domain} is not reachable in FreeSWITCH. sofia_contact returned: {contact}");

        await SendApiAsync($"uuid_transfer {uuid} 'bridge:{contact}' inline", ct);
    }

    /// <summary>True when sofia_contact returned a usable contact URI (not empty, "-ERR", or "error/…").</summary>
    private static bool IsResolvedContact(string? contact) =>
        !string.IsNullOrEmpty(contact)
        && !contact.StartsWith("-ERR", StringComparison.Ordinal)
        && !contact.StartsWith("error/", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve the agent's registered SIP contact, retrying briefly through a transient
    /// registration gap. JsSIP refreshes its REGISTER mid-session; if the WebSocket transport
    /// is reaped between refreshes there's a sub-second window where the browser re-registers
    /// and sofia_contact returns "error/user_not_registered". Without this, an inbound bridge
    /// landing in that window drops the caller. ~2s of retries covers the recovery blip; a
    /// genuinely offline agent still ends in the same failure after the retries.
    /// </summary>
    private async Task<string?> ResolveAgentContactAsync(string extension, string domain, CancellationToken ct)
    {
        const int attempts = 5;
        string? contact = null;
        for (var i = 0; i < attempts; i++)
        {
            contact = await SendApiBodyAsync($"sofia_contact {extension}@{domain}", ct);
            if (IsResolvedContact(contact)) return contact;
            if (i < attempts - 1)
            {
                logger?.LogWarning(
                    "sofia_contact {Ext}@{Domain} → '{Contact}' (attempt {N}/{Total}) — retrying through possible re-registration gap",
                    extension, domain, contact, i + 1, attempts);
                await Task.Delay(500, ct);
            }
        }
        return contact;
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

    /// <summary>uuid_getvar — diagnostic read of a channel variable. Null when the channel is
    /// gone or the variable is unset (FreeSWITCH returns "_undef_").</summary>
    public async Task<string?> GetChannelVarAsync(string uuid, string name, CancellationToken ct = default)
    {
        var body = await SendApiBodyAsync($"uuid_getvar {uuid} {name}", ct);
        if (string.IsNullOrWhiteSpace(body)) return null;
        body = body.Trim();
        return body is "_undef_" || body.StartsWith("-ERR", StringComparison.Ordinal) ? null : body;
    }

    /// <summary>Send any raw ESL api command and return the response body. For test/diagnostic use.</summary>
    public Task<string?> RunCommandAsync(string command, CancellationToken ct = default) =>
        SendApiBodyAsync(command, ct);

    public Task BroadcastAsync(string uuid, string mediaArg, CancellationToken ct = default) =>
        SendApiAsync($"uuid_broadcast {uuid} {mediaArg} aleg", ct);

    public Task BridgeChannelsAsync(string uuid1, string uuid2, CancellationToken ct = default) =>
        SendApiAsync($"uuid_bridge {uuid1} {uuid2}", ct);

    public Task SendDtmfAsync(string uuid, string digits, int durationMs, CancellationToken ct = default) =>
        SendApiAsync($"uuid_send_dtmf {uuid} {digits}@{durationMs}", ct);

    public Task TransferAsync(string uuid, string destination, string dialplan, string context, CancellationToken ct = default) =>
        SendApiAsync($"uuid_transfer {uuid} {destination} {dialplan} {context}", ct);

    public Task RecordAsync(string uuid, string action, string path, int limitSeconds = 0, CancellationToken ct = default) =>
        SendApiAsync(
            limitSeconds > 0
                ? $"uuid_record {uuid} {action} {path} {limitSeconds}"
                : $"uuid_record {uuid} {action} {path}",
            ct);

    public Task StartAudioStreamAsync(string uuid, string wssUrl, string mixType, string sampleRateLabel, string metadata, CancellationToken ct = default) =>
        SendApiAsync($"uuid_audio_stream {uuid} start {wssUrl} {mixType} {sampleRateLabel} {metadata}", ct);

    public Task StopAudioStreamAsync(string uuid, CancellationToken ct = default) =>
        SendApiAsync($"uuid_audio_stream {uuid} stop", ct);

    /// <returns>(uuid, null) on success; (null, errorDetail) on failure so callers can log the cause.</returns>
    public async Task<(string? Uuid, string? Error)> OriginateAndParkAsync(string extension, string domain, string callerNumber, CancellationToken ct = default)
    {
        var contact = await ResolveAgentContactAsync(extension, domain, ct);
        if (!IsResolvedContact(contact))
            return (null, $"sofia_contact {extension}@{domain} → {contact ?? "(null)"}");

        // Pre-assign the agent leg's UUID so we can (a) reliably identify it later and
        // (b) uuid_kill it if the originate fails partway (browser answered but media never
        // came up, park raced, etc.) — otherwise that half-built leg lingers in FreeSWITCH
        // as a zombie, later firing an orphan CHANNEL_HANGUP with no session to clean up.
        var agentUuid = Guid.NewGuid().ToString();

        // origination_caller_id_* populates the From header of the INVITE FreeSWITCH sends to the
        // agent's endpoint — effective_caller_id_* alone does not on an originate, so without this
        // the agent's softphone shows a blank/zeroed caller number on any INVITE it actually rings
        // for (e.g. a queue-callback bridge under a non-auto-answer campaign).
        var vars = $"{{origination_uuid={agentUuid},originate_timeout=30,sip_auto_answer=true," +
                   $"sip_h_Alert-Info=answer-after=0," +
                   $"origination_caller_id_number={callerNumber},origination_caller_id_name={callerNumber}," +
                   $"effective_caller_id_number={callerNumber},effective_caller_id_name={callerNumber}," +
                   $"cc_whisper=true}}";
        var response = await SendApiBodyAsync($"originate {vars}{contact} &park()", ct);

        if (response?.StartsWith("+OK") == true)
        {
            logger?.LogInformation(
                "OriginateAndParkAsync: agent leg {AgentUuid} up ({Ext}@{Domain}), response={Response}",
                agentUuid, extension, domain, response.Trim());
            return (agentUuid, null);
        }

        // Best-effort cleanup of the pre-assigned leg in case it was partially created.
        await SendApiAsync($"uuid_kill {agentUuid} ORIGINATOR_CANCEL", ct);
        logger?.LogWarning(
            "OriginateAndParkAsync: originate failed for {Ext}@{Domain} — killed pre-assigned leg {AgentUuid}. sofia_contact={Contact} response={Response}",
            extension, domain, agentUuid, contact, response ?? "(null)");
        return (null, $"sofia_contact={contact} originate → {response ?? "(null)"}");
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
