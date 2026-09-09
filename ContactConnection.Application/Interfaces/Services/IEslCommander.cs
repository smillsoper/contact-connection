namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Abstraction over FreeSWITCH ESL command sending, used by telephony node handlers.
/// Implemented by EslClient in the API layer and injected into the telephony flow engine.
/// </summary>
public interface IEslCommander
{
    Task KillChannelAsync(string uuid, int causeCode, CancellationToken ct = default);
    Task AnswerChannelAsync(string uuid, CancellationToken ct = default);
    Task HangupChannelAsync(string uuid, CancellationToken ct = default);
    Task BridgeToAgentAsync(string uuid, string extension, string domain, string callerNumber, CancellationToken ct = default);
    Task SetChannelVarAsync(string uuid, string name, string value, CancellationToken ct = default);
    /// <summary>uuid_break to stop any current playback on a channel without hanging it up.</summary>
    Task BreakChannelAsync(string uuid, CancellationToken ct = default);
    /// <summary>
    /// uuid_getvar — reads a channel variable for diagnostics (answer state, RTP packet counts,
    /// etc.). Returns null if the channel is gone or the variable is unset (FreeSWITCH "_undef_").
    /// </summary>
    Task<string?> GetChannelVarAsync(string uuid, string name, CancellationToken ct = default);
    /// <summary>uuid_broadcast to play media on one leg of a parked/bridged channel.</summary>
    Task BroadcastAsync(string uuid, string mediaArg, CancellationToken ct = default);
    /// <summary>uuid_bridge to connect two already-established parked channels.</summary>
    Task BridgeChannelsAsync(string uuid1, string uuid2, CancellationToken ct = default);
    /// <summary>Originate a call to an agent extension with auto-answer and park the channel. Returns (uuid, null) on success, (null, errorDetail) on failure.</summary>
    Task<(string? Uuid, string? Error)> OriginateAndParkAsync(string extension, string domain, string callerNumber, CancellationToken ct = default);
    /// <summary>Send DTMF tones on the specified channel. digits may include 0-9 * # A-D w W; @durationMs sets per-digit tone length.</summary>
    Task SendDtmfAsync(string uuid, string digits, int durationMs, CancellationToken ct = default);

    /// <summary>
    /// uuid_transfer &lt;uuid&gt; &lt;destination&gt; &lt;dialplan&gt; &lt;context&gt; — sends a live (parked) channel
    /// into a dialplan extension. Used by tf_ivr_menu to run FreeSWITCH's play_and_get_digits in the
    /// "ivr_collect" extension (this build has no uuid_execute), which emits a CUSTOM event with the
    /// result and re-parks.
    /// </summary>
    Task TransferAsync(string uuid, string destination, string dialplan, string context, CancellationToken ct = default);

    /// <summary>
    /// uuid_record &lt;uuid&gt; &lt;action&gt; &lt;path&gt; — controls call recording on a live channel.
    /// action is one of start | stop | mask | unmask. mask/unmask fill the recording with
    /// silence while keeping the file wall-clock continuous (PCI-correct — never stop/start
    /// for a sensitive segment). path must be identical across all four actions for one file.
    /// limitSeconds &gt; 0 caps a recording's duration (0 = unlimited).
    /// </summary>
    Task RecordAsync(string uuid, string action, string path, int limitSeconds = 0, CancellationToken ct = default);

    /// <summary>
    /// uuid_audio_stream ... start — opens an outbound WebSocket connection from FreeSWITCH to
    /// wssUrl for streaming TTS playback (mod_audio_stream). metadata should be a short,
    /// space-free correlation token, not the actual request payload — see TtsStreamRelayRequest.
    /// </summary>
    Task StartAudioStreamAsync(string uuid, string wssUrl, string mixType, string sampleRateLabel, string metadata, CancellationToken ct = default);

    /// <summary>
    /// uuid_audio_stream ... stop — tells mod_audio_stream the stream is done. Required: closing
    /// the WebSocket from the server side alone is not enough — the module's underlying
    /// WebSocket library auto-reconnects on any close, so without this call FreeSWITCH retries
    /// the connection indefinitely after synthesis finishes.
    /// </summary>
    Task StopAudioStreamAsync(string uuid, CancellationToken ct = default);
}
