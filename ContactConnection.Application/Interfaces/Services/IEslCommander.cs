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
}
