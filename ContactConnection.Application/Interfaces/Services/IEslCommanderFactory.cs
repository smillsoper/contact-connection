namespace ContactConnection.Application.Interfaces.Services;

/// <summary>
/// Opens a fresh short-lived ESL connection to FreeSWITCH. Used by components that need to
/// issue ESL commands outside a live flow-execution context — e.g. the call-recording
/// watchdog's deferred forced-unmask, which fires long after the node that started the mask
/// has returned and its <see cref="IEslCommander"/> is gone.
///
/// Callers own the returned connection and must dispose it (<c>await using</c>).
/// </summary>
public interface IEslCommanderFactory
{
    Task<IOwnedEslCommander> CreateAsync(CancellationToken ct = default);
}

/// <summary>An <see cref="IEslCommander"/> whose underlying socket the caller is responsible for disposing.</summary>
public interface IOwnedEslCommander : IEslCommander, IAsyncDisposable;
