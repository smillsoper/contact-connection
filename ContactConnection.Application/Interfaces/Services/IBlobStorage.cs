namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Opaque blob store for large binary artifacts that don't belong in Postgres — screen-capture
/// chunks today, merged A/V output and transcoded audio later. Keys are '/'-delimited paths
/// ("screen/{callRecordId}/{id}/000000.webm"). The local-disk implementation backs dev; an
/// Azure Blob implementation slots in behind the same interface for production.
/// </summary>
public interface IBlobStorage
{
    Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
    Task<bool> ExistsAsync(string key, CancellationToken ct = default);
    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Deletes every blob whose key starts with <paramref name="keyPrefix"/> (retention purge, aborted uploads).</summary>
    Task DeletePrefixAsync(string keyPrefix, CancellationToken ct = default);
}
