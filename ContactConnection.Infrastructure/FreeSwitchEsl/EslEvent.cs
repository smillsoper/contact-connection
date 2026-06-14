namespace ContactConnection.Infrastructure.FreeSwitchEsl;

public sealed class EslEvent
{
    public IReadOnlyDictionary<string, string> Headers { get; }
    public string? Body { get; }

    public string? ContentType => Headers.GetValueOrDefault("Content-Type");
    public string? EventName   => Headers.GetValueOrDefault("Event-Name");
    public string? UniqueId    => Headers.GetValueOrDefault("Unique-ID");

    // FreeSWITCH channel variables arrive prefixed with "variable_" in plain events.
    public string? GetVariable(string name) =>
        Headers.GetValueOrDefault($"variable_{name}");

    public EslEvent(Dictionary<string, string> headers, string? body)
    {
        Headers = headers;
        Body    = body;
    }
}
