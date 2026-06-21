namespace ContactConnection.Application.Interfaces.Services;

public interface ITelephonyFlowEngine
{
    Task ExecuteAsync(TelephonyFlowContext context, CancellationToken ct = default);
}

public class TelephonyFlowContext
{
    public required string ChannelUuid { get; init; }
    public required string CallerNumber { get; init; }
    public required string DestinationNumber { get; init; }
    public required Guid TenantId { get; init; }
    public required Guid CampaignId { get; init; }
    public required Guid CallRecordId { get; init; }
    public required string TenantSubdomain { get; init; }
    public required string TenantSchemaName { get; init; }
    public required IEslCommander Esl { get; init; }
    public Dictionary<string, string> Vars { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Channel variables from the CHANNEL_PARK event (e.g. SIP headers as "sip_h_X-Header").</summary>
    public IReadOnlyDictionary<string, string> ChannelVars { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
