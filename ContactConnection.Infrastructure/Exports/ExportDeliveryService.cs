using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.ValueObjects.Exports;
using FluentFTP;
using ICSharpCode.SharpZipLib.Zip;
using PgpCore;
using Renci.SshNet;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// Export file delivery (S180, session 2). See <see cref="IExportDeliveryService"/>. Every connection is pinned or
/// validated — an SFTP target without a pinned host key is refused, never trusted on first use — and secrets are read from
/// the tenant credential store at send time, so they're never in the definition, the run, or a log line.
/// </summary>
public sealed class ExportDeliveryService(ITenantCredentialStore credentials, IEmailService email) : IExportDeliveryService
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private const long MaxEmailBytes = 20 * 1024 * 1024;

    public async Task<ExportDeliveryResult> DeliverAsync(ExportDeliveryRequest r, CancellationToken ct = default)
    {
        var t = r.Target;
        if (t.Validate() is { } invalid) throw new ExportDeliveryException(invalid, permanent: true);

        var (path, name, cleanup) = await EncryptAsync(r, ct);
        try
        {
            return t.Type switch
            {
                ExportDeliveryType.Sftp => await SftpAsync(r.TenantSubdomain, t, path, name, ct),
                ExportDeliveryType.Ftps => new ExportDeliveryResult(await FtpsAsync(r.TenantSubdomain, t, path, name, ct)),
                _ => new ExportDeliveryResult(await EmailAsync(r, path, name, ct)),
            };
        }
        finally
        {
            if (cleanup) TryDelete(path);
        }
    }

    public async Task<ExportConnectionTest> TestAsync(string tenantSubdomain, ExportDeliveryTarget t, CancellationToken ct = default)
    {
        if (t.Validate() is { } invalid) return new(false, invalid, null, null);
        try
        {
            switch (t.Type)
            {
                case ExportDeliveryType.Sftp:
                {
                    string? seen = null;
                    using var client = new SftpClient(await SftpConnectionAsync(tenantSubdomain, t, ct));
                    client.HostKeyReceived += (_, e) => { seen = e.FingerPrintSHA256; e.CanTrust = true; };
                    await client.ConnectAsync(ct);
                    var dir = Dir(t);
                    var exists = dir is null || client.Exists(dir);
                    client.Disconnect();
                    var matches = string.IsNullOrWhiteSpace(t.HostKeyFingerprint) ? (bool?)null : Same(seen, t.HostKeyFingerprint);
                    return new(exists, !exists ? $"Signed in, but the folder {dir} doesn't exist."
                        : dir is null ? "Signed in. No remote folder is set — files will go to the login folder, which many servers don't allow writing to."
                        : $"Signed in; the folder {dir} exists.", seen, matches);
                }
                case ExportDeliveryType.Ftps:
                {
                    string? seen = null;
                    await using var client = await FtpsClientAsync(tenantSubdomain, t, accept: (cert, errors) =>
                    {
                        seen = CertFingerprint(cert);
                        return true;
                    }, ct);
                    await client.Connect(ct);
                    var dir = Dir(t);
                    var exists = dir is null || await client.DirectoryExists(dir, ct);
                    await client.Disconnect(ct);
                    var matches = string.IsNullOrWhiteSpace(t.CertificateFingerprint) ? (bool?)null : Same(seen, t.CertificateFingerprint);
                    return new(exists, exists ? "Signed in." : $"Signed in, but the folder {dir} doesn't exist.", seen, matches);
                }
                default:
                    return new(true, $"Will email {string.Join(", ", t.EmailTo)}.", null, null);
            }
        }
        catch (ExportDeliveryException ex) { return new(false, ex.Message, null, null); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return new(false, Describe(ex), null, null); }
    }

    // ── SFTP ───────────────────────────────────────────────────────────────────

    /// <summary>Host keys follow SSH's "accept-new" (what WinSCP does on first connect): with no key pinned, the first
    /// connection's key is accepted and returned for pinning; once pinned, any other key is refused and nothing is sent.</summary>
    private async Task<ExportDeliveryResult> SftpAsync(string tenant, ExportDeliveryTarget t, string path, string name, CancellationToken ct)
    {
        var acceptNew = string.IsNullOrWhiteSpace(t.HostKeyFingerprint);
        string? seen = null;
        using var client = new SftpClient(await SftpConnectionAsync(tenant, t, ct));
        client.HostKeyReceived += (_, e) => { seen = e.FingerPrintSHA256; e.CanTrust = acceptNew || Same(seen, t.HostKeyFingerprint); };
        try { await client.ConnectAsync(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (!acceptNew && seen is not null && !Same(seen, t.HostKeyFingerprint))
                throw new ExportDeliveryException(
                    $"{t.Name}: the server's host key has changed (now {seen}). Nothing was sent. If the vendor confirms the change, pin the new key.",
                    permanent: true);
            throw new ExportDeliveryException($"{t.Name}: {Describe(ex)}", inner: ex);
        }

        var remote = Remote(t, name);
        try
        {
            await using var file = File.OpenRead(path);
            await client.UploadFileAsync(file, remote, ct);
        }
        // Folder problems don't fix themselves — say what's wrong and don't burn the retries.
        catch (Renci.SshNet.Common.SftpPermissionDeniedException)
        {
            throw new ExportDeliveryException(
                $"{t.Name}: the server refused to write {remote} (permission denied). " +
                (Dir(t) is null ? "No remote folder is set, so it went to the login folder — set the folder the vendor gave you." : "Check the remote folder with the vendor."),
                permanent: true);
        }
        catch (Renci.SshNet.Common.SftpPathNotFoundException)
        {
            throw new ExportDeliveryException($"{t.Name}: the folder {Dir(t)} doesn't exist on the server.", permanent: true);
        }
        client.Disconnect();
        return new ExportDeliveryResult($"sftp://{t.Host}{(remote.StartsWith('/') ? "" : "/")}{remote}", acceptNew ? seen : null);
    }

    private async Task<Renci.SshNet.ConnectionInfo> SftpConnectionAsync(string tenant, ExportDeliveryTarget t, CancellationToken ct)
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(t.PrivateKeyCredential))
        {
            var pem = await Secret(tenant, t.PrivateKeyCredential, t.Name, ct);
            var key = new PrivateKeyFile(new MemoryStream(Encoding.UTF8.GetBytes(pem)));
            methods.Add(new PrivateKeyAuthenticationMethod(t.Username!, key));
        }
        if (!string.IsNullOrWhiteSpace(t.PasswordCredential))
            methods.Add(new PasswordAuthenticationMethod(t.Username!, await Secret(tenant, t.PasswordCredential, t.Name, ct)));
        return new Renci.SshNet.ConnectionInfo(t.Host!, t.Port ?? t.DefaultPort, t.Username!, methods.ToArray()) { Timeout = ConnectTimeout };
    }

    // ── FTPS ───────────────────────────────────────────────────────────────────

    private async Task<string> FtpsAsync(string tenant, ExportDeliveryTarget t, string path, string name, CancellationToken ct)
    {
        var pinned = t.CertificateFingerprint;
        await using var client = await FtpsClientAsync(tenant, t, accept: (cert, errors) =>
            string.IsNullOrWhiteSpace(pinned) ? errors == SslPolicyErrors.None : Same(CertFingerprint(cert), pinned), ct);
        try { await client.Connect(ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExportDeliveryException($"{t.Name}: {Describe(ex)}", inner: ex);
        }
        var remote = Remote(t, name);
        await using (var file = File.OpenRead(path))
        {
            var status = await client.UploadStream(file, remote, FtpRemoteExists.Overwrite, createRemoteDir: false, token: ct);
            if (status == FtpStatus.Failed) throw new ExportDeliveryException($"{t.Name}: the server refused the upload of {remote}.");
        }
        await client.Disconnect(ct);
        return $"ftps://{t.Host}{(remote.StartsWith('/') ? "" : "/")}{remote}";
    }

    private async Task<AsyncFtpClient> FtpsClientAsync(
        string tenant, ExportDeliveryTarget t, Func<X509Certificate?, SslPolicyErrors, bool> accept, CancellationToken ct)
    {
        var password = await Secret(tenant, t.PasswordCredential!, t.Name, ct);
        var client = new AsyncFtpClient(t.Host!, t.Username!, password, t.Port ?? t.DefaultPort);
        client.Config.EncryptionMode = t.FtpsImplicit ? FtpEncryptionMode.Implicit : FtpEncryptionMode.Explicit;
        client.Config.DataConnectionEncryption = true;
        client.Config.ConnectTimeout = (int)ConnectTimeout.TotalMilliseconds;
        client.ValidateCertificate += (_, e) => e.Accept = accept(e.Certificate, e.PolicyErrors);
        return client;
    }

    // ── Email ──────────────────────────────────────────────────────────────────

    private async Task<string> EmailAsync(ExportDeliveryRequest r, string path, string name, CancellationToken ct)
    {
        var t = r.Target;
        var bytes = await File.ReadAllBytesAsync(path, ct);
        if (bytes.LongLength > MaxEmailBytes)
            throw new ExportDeliveryException($"{t.Name}: the file is {bytes.LongLength / 1048576} MB — too big to email (20 MB limit). Use SFTP.", permanent: true);

        var exportName = r.EmailModel.GetValueOrDefault("export") is Dictionary<string, object?> e ? e["name"] : "Export";
        var subject = $"{exportName} — {name}";
        if (!string.IsNullOrWhiteSpace(t.EmailSubject))
        {
            var engine = new ExportTemplateEngine(ExportGenerator.ResolveZone(r.TimeZone) ?? TimeZoneInfo.Utc);
            subject = (await engine.RenderAsync(t.EmailSubject, new Dictionary<string, object?>(r.EmailModel) { ["file_name"] = name })).Trim();
        }
        await email.SendAsync(new EmailMessage
        {
            To = t.EmailTo,
            Subject = subject,
            HtmlBody = $"<p>The attached file <b>{System.Net.WebUtility.HtmlEncode(name)}</b> was sent automatically by ContactConnection.</p>",
            Attachments = [new EmailAttachment(name, bytes, name == r.FileName ? r.ContentType : "application/octet-stream")],
        }, ct);
        return string.Join(", ", t.EmailTo);
    }

    // ── Encryption ─────────────────────────────────────────────────────────────

    private async Task<(string Path, string Name, bool Cleanup)> EncryptAsync(ExportDeliveryRequest r, CancellationToken ct)
    {
        var t = r.Target;
        if (t.Encryption == ExportEncryption.None) return (r.LocalPath, r.FileName, false);
        var output = r.LocalPath + "." + t.Encryption + "." + Guid.NewGuid().ToString("N")[..8];
        try
        {
            if (t.Encryption == ExportEncryption.Pgp)
            {
                var pgp = new PGP(new EncryptionKeys(t.PgpPublicKey!));
                await using var input = File.OpenRead(r.LocalPath);
                await using var outStream = File.Create(output);
                await pgp.EncryptAsync(input, outStream, armor: false, withIntegrityCheck: true, name: r.FileName);
                return (output, r.FileName + ".pgp", true);
            }

            var password = await Secret(r.TenantSubdomain, t.ZipPasswordCredential!, t.Name, ct);
            await using (var outStream = File.Create(output))
            await using (var zip = new ZipOutputStream(outStream) { IsStreamOwner = false, Password = password })
            {
                zip.SetLevel(9);
                zip.PutNextEntry(new ZipEntry(r.FileName) { AESKeySize = 256, DateTime = DateTime.Now });
                await using (var input = File.OpenRead(r.LocalPath)) await input.CopyToAsync(zip, ct);
                zip.CloseEntry();
                zip.Finish();
            }
            return (output, Path.GetFileNameWithoutExtension(r.FileName) + ".zip", true);
        }
        catch (Exception ex) when (ex is not (OperationCanceledException or ExportDeliveryException))
        {
            TryDelete(output);
            throw new ExportDeliveryException($"{t.Name}: couldn't encrypt the file — {ex.Message}", permanent: true, inner: ex);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<string> Secret(string tenant, string key, string target, CancellationToken ct) =>
        await credentials.GetForTenantAsync(tenant, key, ct) is { Length: > 0 } v
            ? v
            : throw new ExportDeliveryException($"{target}: the stored credential '{key}' wasn't found.", permanent: true);

    private static string? Dir(ExportDeliveryTarget t) =>
        string.IsNullOrWhiteSpace(t.RemoteDirectory) ? null : t.RemoteDirectory.Trim().TrimEnd('/') is { Length: > 0 } d ? d : "/";

    private static string Remote(ExportDeliveryTarget t, string name) => Dir(t) is { } d ? (d == "/" ? "/" + name : $"{d}/{name}") : name;

    public static string CertFingerprint(X509Certificate? cert) =>
        cert is null ? "" : Convert.ToBase64String(SHA256.HashData(cert.GetRawCertData())).TrimEnd('=');

    /// <summary>Fingerprints compare ignoring a "SHA256:" prefix, base64 padding and surrounding space.</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && Norm(a) == Norm(b);

    private static string Norm(string s)
    {
        s = s.Trim();
        if (s.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase)) s = s[7..];
        return s.TrimEnd('=');
    }

    private static string Describe(Exception ex) => ex switch
    {
        Renci.SshNet.Common.SshAuthenticationException => "the server rejected the user name / password or key.",
        System.Net.Sockets.SocketException se => $"couldn't reach the server ({se.SocketErrorCode}).",
        TimeoutException or Renci.SshNet.Common.SshOperationTimeoutException => "the server didn't answer in time.",
        _ => ex.Message,
    };

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { /* temp cleanup is best-effort */ }
    }
}

/// <summary>Resolves the real <see cref="IEmailService"/> on first send (see the DI registration).</summary>
public sealed class DeferredEmailService(IServiceProvider services) : IEmailService
{
    private IEmailService Email => (IEmailService)(services.GetService(typeof(IEmailService))
        ?? throw new ExportDeliveryException("Email isn't configured on this server."));

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default) =>
        Wrap(() => Email.SendAsync(to, subject, htmlBody, ct));

    public Task SendAsync(EmailMessage message, CancellationToken ct = default) => Wrap(() => Email.SendAsync(message, ct));

    private static async Task Wrap(Func<Task> send)
    {
        try { await send(); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not configured", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExportDeliveryException($"Email isn't configured on this server ({ex.Message})", permanent: true, inner: ex);
        }
    }
}
