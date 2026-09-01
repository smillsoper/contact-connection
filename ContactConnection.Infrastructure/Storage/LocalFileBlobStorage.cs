using ContactConnection.Application.Interfaces.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Storage;

/// <summary>
/// <see cref="IBlobStorage"/> backed by the local filesystem under <c>Storage:LocalRoot</c>
/// (default "storage/"). Keys map straight to relative paths; each segment is validated so a
/// key can never escape the root.
/// </summary>
public sealed class LocalFileBlobStorage : IBlobStorage
{
    private readonly string _root;
    private readonly ILogger<LocalFileBlobStorage> _logger;

    public LocalFileBlobStorage(IConfiguration config, ILogger<LocalFileBlobStorage> logger)
    {
        _root = Path.GetFullPath(config["Storage:LocalRoot"] ?? "storage");
        _logger = logger;
        Directory.CreateDirectory(_root);
    }

    public async Task PutAsync(string key, Stream content, string contentType, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, ct);
        _logger.LogDebug("Blob put {Key} ({Bytes} bytes)", key, file.Length);
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        Stream? s = File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)
            : null;
        return Task.FromResult(s);
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(File.Exists(ResolvePath(key)));

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        var path = ResolvePath(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public Task DeletePrefixAsync(string keyPrefix, CancellationToken ct = default)
    {
        var dir = ResolvePath(keyPrefix);
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        else if (File.Exists(dir))
            File.Delete(dir);
        return Task.CompletedTask;
    }

    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Blob key must not be empty.", nameof(key));

        var segments = key.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var seg in segments)
            if (seg is "." or ".." || seg.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException($"Invalid blob key segment '{seg}'.", nameof(key));

        var full = Path.GetFullPath(Path.Combine(_root, Path.Combine(segments)));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
            throw new ArgumentException("Blob key resolves outside the storage root.", nameof(key));
        return full;
    }
}
