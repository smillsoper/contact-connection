using System.Net;
using System.Text;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Billing;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Porting;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Number porting (S184). Tenant users with numbers.port request port-ins; the carrier account's owner fills in and
/// e-signs SignalWire's LOA through a secure link (no sign-in); the porting mailbox gets everything SignalWire's port form
/// asks for; the Portal's Porting queue (Owner + Support) tracks each order to completion.
/// </summary>
public static class PortingEndpoints
{
    private const long MaxBillBytes = 20 * 1024 * 1024;
    private const int MaxSignatureBytes = 400 * 1024;
    private static readonly HashSet<string> BillExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png" };

    public static IEndpointRouteBuilder MapPortingEndpoints(this IEndpointRouteBuilder app)
    {
        var t = app.MapGroup("/api/v1/porting").RequireAuthorization("NumbersPort");
        t.MapPost("scrub", Scrub);
        t.MapGet("campaigns", Campaigns);
        t.MapGet("flows", Flows);
        t.MapPut("orders/{id:guid}/pre-assign", PreAssign);
        t.MapPost("orders", Create);
        t.MapGet("orders", TenantList);
        t.MapGet("orders/{id:guid}", TenantGet);
        t.MapGet("orders/{id:guid}/files/{kind}", TenantFile);
        t.MapPost("orders/{id:guid}/resend", Resend);
        t.MapPost("orders/{id:guid}/cancel", TenantCancel);

        // The signer — no sign-in; the token (from the emailed link) is the key, sent in the body / a header, never the URL.
        var s = app.MapGroup("/api/v1/porting/sign").AllowAnonymous().RequireRateLimiting("client-auth");
        s.MapPost("view", SignerView);
        s.MapPost("bill", SignerBill).DisableAntiforgery();
        s.MapPost("submit", SignerSubmit);

        var p = app.MapGroup("/api/v1/portal/porting").RequireAuthorization("PlatformAdmin");
        p.MapGet("", PortalList);
        p.MapGet("{id:guid}", PortalGet);
        p.MapGet("{id:guid}/files/{kind}", PortalFile);
        p.MapPost("{id:guid}/submitted", Submitted);
        p.MapPost("{id:guid}/foc", Foc);
        p.MapPost("{id:guid}/complete", Complete);
        p.MapPost("{id:guid}/correction", Correction);
        p.MapPost("{id:guid}/cancel", PortalCancel);
        p.MapPost("{id:guid}/note", Note);
        return app;
    }

    // ── Shared ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static string Who(HttpContext http) =>
        $"{http.User.FindFirst("given_name")?.Value} {http.User.FindFirst("family_name")?.Value}".Trim() is { Length: > 0 } n ? n : "ContactConnection";

    private static string BaseUrl(IConfiguration c) => (c["App:BaseUrl"] ?? "https://contactconnection.io").TrimEnd('/');
    private static string PortalUrl(IConfiguration c) => (c["App:PortalUrl"] ?? "https://admin.contactconnection.io").TrimEnd('/');
    private static string Mailbox(IConfiguration c) => c["Porting:Mailbox"] ?? "platform-porting@contactconnection.io";
    private static string Kind(string k) => k == PortOrderKind.TollFree ? "toll-free" : "local";
    private static string H(string? s) => WebUtility.HtmlEncode(s ?? "");

    private static object Summary(PortOrder o, string? tenantName = null) => new
    {
        o.Id, o.Reference, tenantName, kind = o.Kind, numberCount = o.Numbers.Count, o.Numbers, o.Status, o.SignerEmail,
        o.RequestedByName, o.CreatedAt, o.UpdatedAt, o.SignedAt, signatureDaysLeft = o.SignatureDaysLeft(DateTimeOffset.UtcNow),
        o.SignalWireOrderNumber, o.FocDate, o.CompletedAt, o.EndUserName, label = o.Label,
        o.PreAssignCampaignId, o.PreAssignFlowId, o.PreAssignTelephonyFlowId, o.NumbersLoadedAt,
    };

    private static async Task SendAsync(IEmailService email, ILogger log, EmailMessage m, CancellationToken ct)
    {
        try { await email.SendAsync(m, ct); }
        catch (Exception ex) { log.LogError(ex, "Porting: email '{Subject}' failed", m.Subject); }
    }

    private static string Wrap(string inner) =>
        $"<div style=\"font-family:Segoe UI,Arial,sans-serif;font-size:14px;color:#111827;line-height:1.5\">{inner}</div>";

    private static async Task SendSigningLinkAsync(PortOrder o, string token, Tenant tenant, IEmailService email, IConfiguration config, ILogger log, CancellationToken ct)
    {
        var link = $"{BaseUrl(config)}/port-sign#t={token}";
        var correction = o.CorrectionMessage is { } msg
            ? $"<p style=\"background:#fef3c7;padding:10px;border-radius:6px\"><b>Please correct:</b> {H(msg)}</p>" : "";
        await SendAsync(email, log, new EmailMessage
        {
            To = [o.SignerEmail], FromName = "ContactConnection Porting", ReplyTo = Mailbox(config),
            Subject = o.CorrectionMessage is null ? $"Please authorize moving your phone numbers ({o.Reference})" : $"Correction needed — phone number authorization ({o.Reference})",
            HtmlBody = Wrap($"""
                <p>{H(o.RequestedByName)} at {H(tenant.DisplayName ?? tenant.Name)} is moving {o.Numbers.Count} {Kind(o.Kind)} phone number(s) to
                ContactConnection, whose carrier is SignalWire. As the owner of the account with the current phone company, you need to fill
                in and sign SignalWire's letter of authorization.</p>
                {correction}
                <p><a href="{link}" style="background:#2563eb;color:#fff;padding:10px 16px;border-radius:6px;text-decoration:none">Review and sign</a></p>
                <p>Have a recent bill from the current phone company ready — you'll upload it, and the names and address must match it exactly.
                This link works for 14 days. If you didn't expect this, just ignore it.</p>
                """),
        }, ct);
    }

    private static async Task NotifyRequesterAsync(PortOrder o, string subject, string text, IEmailService email, ILogger log, CancellationToken ct,
        IReadOnlyList<EmailAttachment>? attachments = null) =>
        await SendAsync(email, log, new EmailMessage
        {
            To = [o.RequestedByEmail], FromName = "ContactConnection Porting", Subject = $"{o.Reference}: {subject}",
            HtmlBody = Wrap($"<p>{text}</p><p style=\"color:#6b7280\">Port {o.Reference} — {o.Numbers.Count} {Kind(o.Kind)} number(s). Follow it on the Number Porting page.</p>"),
            Attachments = attachments ?? [],
        }, ct);

    // ── Tenant ───────────────────────────────────────────────────────────────────────────────────────────────────

    public record ScrubRequest(string? Text);

    /// <summary>What happens to each pasted number: portable local / toll-free, already this account's, unavailable, or not a number.</summary>
    private static async Task<IResult> Scrub(ScrubRequest req, TenantContext tc, ContactConnectionDbContext master, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        return Results.Ok(await ScrubAsync(req.Text, tenant.Id, master, ct));
    }

    public sealed record ScrubResult(List<string> Local, List<string> TollFree, List<string> AlreadyYours, List<string> Unavailable,
        List<string> InProgress, List<string> Invalid);

    private static async Task<ScrubResult> ScrubAsync(string? text, Guid tenantId, ContactConnectionDbContext master, CancellationToken ct)
    {
        var parsed = PortNumbers.Parse(text);
        // Older routing rows hold 10 / 11 digits instead of E.164 — match every spelling, then normalize back.
        var spellings = parsed.Numbers.SelectMany(n => new[] { n, n[2..], n[1..] }).ToList();
        var routes = (await master.PhoneNumberRoutings.AsNoTracking().Where(r => spellings.Contains(r.Number)).ToListAsync(ct))
            .Select(r => (Number: PortNumbers.Parse(r.Number).Numbers.FirstOrDefault(), Route: r)).ToList();
        var open = await master.PortOrders.AsNoTracking()
            .Where(o => o.Status != PortOrderStatus.Completed && o.Status != PortOrderStatus.Cancelled)
            .Select(o => o.Numbers).ToListAsync(ct);
        var porting = open.SelectMany(n => n).ToHashSet();
        var r = new ScrubResult([], [], [], [], [], parsed.Invalid);
        foreach (var n in parsed.Numbers)
        {
            // Anything already on the platform (even released) is already with our carrier — it can't be ported in. The live
            // row wins if a number appears in more than one spelling.
            var route = routes.Where(x => x.Number == n).Select(x => x.Route).OrderBy(r => r.IsReleased).FirstOrDefault();
            if (route is not null && route.TenantId == tenantId) r.AlreadyYours.Add(n);
            // Another account's number — never say whose.
            else if (route is not null) r.Unavailable.Add(n);
            else if (porting.Contains(n)) r.InProgress.Add(n);
            else if (PortNumbers.IsTollFree(n)) r.TollFree.Add(n);
            else r.Local.Add(n);
        }
        return r;
    }

    private static async Task<IResult> Campaigns(ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        await using var db = dbf.Create();
        return Results.Ok(await db.Campaigns.AsNoTracking().Where(c => c.Status == CampaignStatus.Active).OrderBy(c => c.Name)
            .Select(c => new { c.Id, c.Name }).ToListAsync(ct));
    }

    private static async Task<IResult> Flows(ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        await using var db = dbf.Create();
        return Results.Ok(await db.Flows.AsNoTracking().Where(f => f.IsActive && f.PublishedDefinition != null).OrderBy(f => f.Name)
            .Select(f => new { f.Id, f.Name, type = f.FlowType, f.CampaignId }).ToListAsync(ct));
    }

    public record PreAssignRequest(Guid? CampaignId, Guid? FlowId, Guid? TelephonyFlowId);

    /// <summary>Where the numbers go once the port date is confirmed — changeable until they're loaded.</summary>
    private static async Task<IResult> PreAssign(Guid id, PreAssignRequest req, HttpContext http, TenantContext tc, ContactConnectionDbContext master,
        ScopedTenantDbContextFactory dbf, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (o is null) return Results.NotFound();
        await using var db = dbf.Create();
        if (req.CampaignId is { } c && !await db.Campaigns.AnyAsync(x => x.Id == c, ct)) return Results.BadRequest(new { error = "That campaign doesn't exist." });
        try { o.SetPreAssignment(req.CampaignId, req.FlowId, req.TelephonyFlowId, Who(http), DateTimeOffset.UtcNow); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = e.Message }); }
        await master.SaveChangesAsync(ct);
        return Results.Ok(Summary(o));
    }

    public record OrderGroup(string? Numbers, string? CurrentProvider, string? AccountType, string? EndUserName, string? SignerEmail, Guid? PreAssignCampaignId);
    public record CreateRequest(List<OrderGroup>? Groups);

    /// <summary>Each group = one carrier account at one service address. Mixed local / toll-free groups split in two.</summary>
    private static async Task<IResult> Create(CreateRequest req, HttpContext http, TenantContext tc, ContactConnectionDbContext master,
        IEmailService email, IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant || !Guid.TryParse(http.User.FindFirst("sub")?.Value, out var me)) return Results.Unauthorized();
        var groups = req.Groups ?? [];
        if (groups.Count == 0) return Results.BadRequest(new { error = "Add the numbers to port." });
        var who = Who(http);
        var myEmail = http.User.FindFirst("email")?.Value ?? "";
        var now = DateTimeOffset.UtcNow;
        var created = new List<(PortOrder Order, string Token)>();
        foreach (var g in groups)
        {
            var scrub = await ScrubAsync(g.Numbers, tenant.Id, master, ct);
            if (scrub.Invalid.Count > 0) return Results.BadRequest(new { error = $"Not a phone number: {string.Join(", ", scrub.Invalid.Take(5))}" });
            if (scrub.AlreadyYours.Count + scrub.Unavailable.Count + scrub.InProgress.Count > 0)
                return Results.BadRequest(new { error = "Remove the numbers that can't be ported (already yours, unavailable, or already being ported) and try again." });
            try
            {
                foreach (var (kind, numbers) in new[] { (PortOrderKind.Local, scrub.Local), (PortOrderKind.TollFree, scrub.TollFree) })
                {
                    if (numbers.Count == 0) continue;
                    var o = PortOrder.Create(tenant.Id, kind, numbers, g.AccountType ?? "Business", g.EndUserName ?? tenant.DisplayName ?? tenant.Name,
                        g.CurrentProvider, g.PreAssignCampaignId, me, who, myEmail, g.SignerEmail ?? "", now);
                    created.Add((o, o.IssueSigningLink(now)));
                }
            }
            catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        }
        if (created.Count == 0) return Results.BadRequest(new { error = "Add the numbers to port." });
        master.PortOrders.AddRange(created.Select(c => c.Order));
        await master.SaveChangesAsync(ct);
        var log = logs.CreateLogger("Porting");
        foreach (var (o, token) in created) await SendSigningLinkAsync(o, token, tenant, email, config, log, ct);
        return Results.Ok(created.Select(c => Summary(c.Order)));
    }

    private static async Task<IResult> TenantList(TenantContext tc, ContactConnectionDbContext master, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var rows = await master.PortOrders.AsNoTracking().Where(o => o.TenantId == tenant.Id).OrderByDescending(o => o.CreatedAt).ToListAsync(ct);
        return Results.Ok(rows.Select(o => Summary(o)));
    }

    private static async Task<IResult> TenantGet(Guid id, TenantContext tc, ContactConnectionDbContext master, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var o = await master.PortOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (o is null) return Results.NotFound();
        return Results.Ok(new
        {
            order = Summary(o), o.Events, o.AccountType, o.CurrentProviderHint, o.PreAssignCampaignId, o.CorrectionMessage,
            hasLoa = o.LoaFileKey is not null, hasCertificate = o.CertificateFileKey is not null,
        });
    }

    /// <summary>The tenant can download the signed LOA and the signature record (not the signer's bill).</summary>
    private static async Task<IResult> TenantFile(Guid id, string kind, TenantContext tc, ContactConnectionDbContext master, IBlobStorage blobs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var o = await master.PortOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (o is null || kind is not ("loa" or "certificate")) return Results.NotFound();
        return await FileAsync(o, kind, blobs, ct);
    }

    private static async Task<IResult> FileAsync(PortOrder o, string kind, IBlobStorage blobs, CancellationToken ct)
    {
        var (key, name, type) = kind switch
        {
            "loa" => (o.LoaFileKey, $"{o.Reference} LOA.pdf", "application/pdf"),
            "certificate" => (o.CertificateFileKey, $"{o.Reference} signature record.pdf", "application/pdf"),
            "bill" => (o.BillFileKey, $"{o.Reference} bill - {o.BillFileName}", "application/octet-stream"),
            _ => (null, "", ""),
        };
        if (kind == "numbers") return Results.File(LoaDocuments.NumbersCsv(o), "text/csv", $"{o.Reference} numbers.csv");
        if (key is null) return Results.NotFound();
        var stream = await blobs.OpenReadAsync(key, ct);
        return stream is null ? Results.NotFound() : Results.File(stream, type, name);
    }

    public record ResendRequest(string? SignerEmail);

    private static async Task<IResult> Resend(Guid id, ResendRequest req, HttpContext http, TenantContext tc, ContactConnectionDbContext master,
        IEmailService email, IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (o is null) return Results.NotFound();
        if (o.Status is not (PortOrderStatus.AwaitingSignature or PortOrderStatus.NeedsCorrection))
            return Results.BadRequest(new { error = "This order is already signed." });
        var now = DateTimeOffset.UtcNow;
        try { if (!string.IsNullOrWhiteSpace(req.SignerEmail) && req.SignerEmail.Trim() != o.SignerEmail) o.ChangeSignerEmail(req.SignerEmail, Who(http), now); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        var token = o.IssueSigningLink(now);
        o.Log(now, Who(http), $"Signing link re-sent to {o.SignerEmail}");
        await master.SaveChangesAsync(ct);
        await SendSigningLinkAsync(o, token, tenant, email, config, logs.CreateLogger("Porting"), ct);
        return Results.Ok(Summary(o));
    }

    public record CancelRequest(string? Reason);

    private static async Task<IResult> TenantCancel(Guid id, CancelRequest req, HttpContext http, TenantContext tc, ContactConnectionDbContext master,
        IEmailService email, IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (o is null) return Results.NotFound();
        if (o.Status is PortOrderStatus.Submitted or PortOrderStatus.FocConfirmed)
            return Results.BadRequest(new { error = "This port is already with SignalWire — ask ContactConnection to cancel it." });
        try { o.Cancel(req.Reason ?? "", Who(http), DateTimeOffset.UtcNow); }
        catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
        await master.SaveChangesAsync(ct);
        if (o.SignedAt is not null)
            await SendAsync(email, logs.CreateLogger("Porting"), new EmailMessage
            {
                To = [Mailbox(config)], FromName = "ContactConnection Porting", Subject = $"{o.Reference} cancelled by {tenant.Name}",
                HtmlBody = Wrap($"<p>{H(Who(http))} cancelled port {o.Reference} before it was submitted.</p>"),
            }, ct);
        return Results.Ok(Summary(o));
    }

    // ── The signer ───────────────────────────────────────────────────────────────────────────────────────────────

    public record TokenRequest(string? Token);

    private static async Task<PortOrder?> ByTokenAsync(ContactConnectionDbContext master, string? token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 100) return null;
        var hash = PortOrder.HashToken(token.Trim());
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.TokenHash == hash, ct);
        return o is not null && o.CanSign(DateTimeOffset.UtcNow) ? o : null;
    }

    private static IResult Expired() =>
        Results.Json(new { error = "This link has expired or was replaced by a newer one. Ask the person who sent it for a new link." }, statusCode: 410);

    private static async Task<IResult> SignerView(TokenRequest req, ContactConnectionDbContext master, CancellationToken ct)
    {
        var o = await ByTokenAsync(master, req.Token, ct);
        if (o is null) return Expired();
        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == o.TenantId, ct);
        return Results.Ok(new
        {
            o.Reference, o.Kind, numbers = o.Numbers.Select(PortNumbers.Display), o.EndUserName, o.AccountType,
            currentProvider = o.Signer?.CurrentProvider ?? o.CurrentProviderHint, tenantName = tenant.DisplayName ?? tenant.Name,
            o.RequestedByName, o.CorrectionMessage, previous = o.Signer, o.PinNotApplicable,
            bill = o.BillFileName, o.TokenExpiresAt,
        });
    }

    /// <summary>The bill copy (raw body; X-Port-Token header). Replaces an earlier upload.</summary>
    private static async Task<IResult> SignerBill(string? name, HttpContext http, ContactConnectionDbContext master, IBlobStorage blobs, CancellationToken ct)
    {
        var o = await ByTokenAsync(master, http.Request.Headers["X-Port-Token"].ToString(), ct);
        if (o is null) return Expired();
        var fileName = ChatFile.CleanName(name);
        if (!BillExtensions.Contains(Path.GetExtension(fileName))) return Results.BadRequest(new { error = "Upload a PDF, Word document, JPG or PNG." });
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBillBytes) return Results.BadRequest(new { error = "The bill can be up to 20 MB." });
        }
        if (buffer.Length == 0) return Results.BadRequest(new { error = "That file is empty." });
        var key = $"porting/{o.TenantId}/{o.Id}/bill-{Guid.NewGuid():N}{Path.GetExtension(fileName).ToLowerInvariant()}";
        buffer.Position = 0;
        var old = o.BillFileKey;
        await blobs.PutAsync(key, buffer, "application/octet-stream", ct);
        o.AttachBill(key, fileName, (http.Request.ContentType ?? "application/octet-stream").Split(';')[0]);
        await master.SaveChangesAsync(ct);
        if (old is not null) { try { await blobs.DeleteAsync(old, ct); } catch { /* best effort */ } }
        return Results.Ok(new { name = fileName });
    }

    public record SubmitRequest(string? Token, PortSignerDetails? Details, string? Pin, bool PinNotApplicable, string? SignatureName,
        string? SignaturePng, bool Consent);

    private static async Task<IResult> SignerSubmit(SubmitRequest req, HttpContext http, ContactConnectionDbContext master, IBlobStorage blobs,
        ISensitiveDataProtector protector, IEmailService email, IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        var o = await ByTokenAsync(master, req.Token, ct);
        if (o is null) return Expired();
        if (!req.Consent) return Results.BadRequest(new { error = "Tick the box to agree to sign electronically." });
        if (req.Details is null) return Results.BadRequest(new { error = "Fill in the account details." });
        if (!protector.IsConfigured) return Results.Json(new { error = "Porting isn't available right now — please try again later." }, statusCode: 503);
        byte[] png;
        try
        {
            var data = (req.SignaturePng ?? "").Split(',').Last();
            png = Convert.FromBase64String(data);
        }
        catch (FormatException) { return Results.BadRequest(new { error = "Draw your signature." }); }
        if (png.Length is < 100 or > MaxSignatureBytes || ChatFile.SniffImageType(png.AsSpan(0, Math.Min(16, png.Length))) != "image/png")
            return Results.BadRequest(new { error = "Draw your signature." });

        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == o.TenantId, ct);
        var now = DateTimeOffset.UtcNow;
        var pin = string.IsNullOrWhiteSpace(req.Pin) ? null : protector.Protect(req.Pin.Trim());
        var ip = http.Request.Headers["CF-Connecting-IP"].FirstOrDefault() ?? http.Connection.RemoteIpAddress?.ToString() ?? "";
        try { o.Sign(req.Details, pin, req.PinNotApplicable && pin is null, req.SignatureName ?? "", ip, http.Request.Headers.UserAgent.ToString(), now); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        catch (InvalidOperationException) { return Expired(); }

        // SignalWire's LOA, filled in and signed; then the separate signature record.
        var zone = UsageMeter.ZoneFor(tenant);
        var signedDate = TimeZoneInfo.ConvertTime(now, zone).ToString("MM/dd/yyyy");
        var loa = LoaDocuments.FillLoa(o, new LoaDocuments.FormContext(config["SignalWire:SpaceName"] ?? "", tenant.SignalWireProjectId ?? "", signedDate, png));
        var sha = LoaDocuments.Sha256(loa);
        var cert = LoaDocuments.Certificate(o, sha, tenant.DisplayName ?? tenant.Name, zone.Id, zone);
        var loaKey = $"porting/{o.TenantId}/{o.Id}/loa-{now:yyyyMMddHHmmss}.pdf";
        var certKey = $"porting/{o.TenantId}/{o.Id}/certificate-{now:yyyyMMddHHmmss}.pdf";
        await blobs.PutAsync(loaKey, new MemoryStream(loa), "application/pdf", ct);
        await blobs.PutAsync(certKey, new MemoryStream(cert), "application/pdf", ct);
        o.AttachDocuments(loaKey, sha, certKey);
        await master.SaveChangesAsync(ct);

        var log = logs.CreateLogger("Porting");
        var loaFile = new EmailAttachment($"{o.Reference} LOA.pdf", loa, "application/pdf");
        var certFile = new EmailAttachment($"{o.Reference} signature record.pdf", cert, "application/pdf");
        await SendAsync(email, log, PortingMailboxEmail(o, tenant, loaFile, certFile, await ReadAsync(blobs, o.BillFileKey, ct), config), ct);
        await SendAsync(email, log, new EmailMessage
        {
            To = [o.SignerEmail], FromName = "ContactConnection Porting", Subject = $"Your signed authorization ({o.Reference})",
            HtmlBody = Wrap($"<p>Thank you — here's a copy of the letter of authorization you signed and the record of your signature. " +
                            "Keep service with your current phone company until the move is complete.</p>"),
            Attachments = [loaFile, certFile],
        }, ct);
        await NotifyRequesterAsync(o, "signed", $"{H(o.SignatureName)} signed the letter of authorization. ContactConnection will now submit the port.", email, log, ct, [loaFile, certFile]);
        return Results.Ok(new { o.Reference, signedAt = o.SignedAt });
    }

    private static async Task<byte[]?> ReadAsync(IBlobStorage blobs, string? key, CancellationToken ct)
    {
        if (key is null) return null;
        await using var s = await blobs.OpenReadAsync(key, ct);
        if (s is null) return null;
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    /// <summary>Everything SignalWire's port-in form asks, in its order — the porting desk copies it straight across. The PIN
    /// stays in the Portal (never emailed).</summary>
    private static EmailMessage PortingMailboxEmail(PortOrder o, Tenant tenant, EmailAttachment loa, EmailAttachment cert, byte[]? bill, IConfiguration config)
    {
        var s = o.Signer!;
        var rows = new (string, string)[]
        {
            ("Contact Name", config["Porting:ContactName"] ?? "(set Porting:ContactName)"),
            ("Contact Email", config["Porting:ContactEmail"] ?? Mailbox(config)),
            ("Project ID", tenant.SignalWireProjectId ?? "⚠ not set — add it on Manage Tenant"),
            ("Numbers to Port", string.Join("<br>", o.Numbers)),
            ("Services to Port", o.Services),
            ("10DLC Campaign ID", "N/A"),
            ("Current Provider Name", s.CurrentProvider ?? o.CurrentProviderHint ?? ""),
            ("Current Provider Account Number", string.IsNullOrWhiteSpace(s.AccountNumber) ? "N/A" : s.AccountNumber),
            ("Account Type", o.AccountType),
            ("Authorized Name on the Account", s.AuthorizedName),
            ("Billing Phone Number", s.BillingPhone),
            ("End User Name", o.EndUserName),
            ("Phone Service Address", s.ServiceAddressLine),
            ("PIN", o.PinNotApplicable ? "N/A" : "In the Portal (Porting → " + o.Reference + ") — not sent by email"),
        };
        var table = string.Concat(rows.Select(r =>
            $"<tr><td style=\"padding:5px 10px;color:#6b7280;vertical-align:top;white-space:nowrap\">{H(r.Item1)}</td><td style=\"padding:5px 10px\">{(r.Item1 == "Numbers to Port" ? r.Item2 : H(r.Item2))}</td></tr>"));
        var attachments = new List<EmailAttachment> { loa, cert, new($"{o.Reference} numbers.csv", LoaDocuments.NumbersCsv(o), "text/csv") };
        if (bill is not null) attachments.Add(new($"{o.Reference} bill - {o.BillFileName}", bill, "application/octet-stream"));
        return new EmailMessage
        {
            To = [Mailbox(config)], FromName = "ContactConnection Porting",
            Subject = $"Ready to submit: {o.Reference} — {tenant.Name}, {o.Numbers.Count} {Kind(o.Kind)} number(s)",
            HtmlBody = Wrap($"""
                <p><b>{H(tenant.Name)}</b> — port {o.Reference} is signed and ready to enter in SignalWire's port-in form
                ({(o.Kind == PortOrderKind.TollFree ? "use the Toll-Free LOA" : "use the Local TN LOA")} attached, plus the bill copy).</p>
                <table style="border-collapse:collapse;border:1px solid #e5e7eb">{table}</table>
                <p>When it's submitted, record SignalWire's order number in the Portal:
                <a href="{PortalUrl(config)}/portal/porting/{o.Id}">{o.Reference}</a>. The signature is valid for SignalWire until
                {(o.SignedAt!.Value + PortOrder.SignatureValidFor):yyyy-MM-dd}.</p>
                """),
            Attachments = attachments,
        };
    }

    // ── The Portal (porting desk) ────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> PortalList(string? status, ContactConnectionDbContext master, CancellationToken ct)
    {
        var q = master.PortOrders.AsNoTracking();
        if (status == "open") q = q.Where(o => o.Status != PortOrderStatus.Completed && o.Status != PortOrderStatus.Cancelled);
        else if (!string.IsNullOrEmpty(status)) q = q.Where(o => o.Status == status);
        var rows = await q.OrderByDescending(o => o.UpdatedAt).Take(500).ToListAsync(ct);
        var tenantIds = rows.Select(r => r.TenantId).Distinct().ToList();
        var names = await master.Tenants.AsNoTracking().Where(t => tenantIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        return Results.Ok(rows.Select(o => Summary(o, names.GetValueOrDefault(o.TenantId))));
    }

    private static async Task<IResult> PortalGet(Guid id, ContactConnectionDbContext master, ISensitiveDataProtector protector, IConfiguration config, CancellationToken ct)
    {
        var o = await master.PortOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Results.NotFound();
        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == o.TenantId, ct);
        string? pin = null;
        if (o.PinProtected is not null) { try { pin = protector.Unprotect(o.PinProtected); } catch { pin = "(couldn't decrypt)"; } }
        var s = o.Signer;
        return Results.Ok(new
        {
            order = Summary(o, tenant.Name), o.Events, o.CorrectionMessage, tenantSubdomain = tenant.Subdomain,
            files = new { loa = o.LoaFileKey is not null, certificate = o.CertificateFileKey is not null, bill = o.BillFileKey is not null, billName = o.BillFileName },
            o.SensitivePurgedAt,
            // SignalWire's port-in form, in its order.
            form = s is null ? null : new[]
            {
                new { label = "Contact Name", value = config["Porting:ContactName"] ?? "" },
                new { label = "Contact Email", value = config["Porting:ContactEmail"] ?? Mailbox(config) },
                new { label = "Project ID", value = tenant.SignalWireProjectId ?? "" },
                new { label = "Numbers to Port", value = string.Join("\n", o.Numbers) },
                new { label = "Services to Port", value = o.Services },
                new { label = "10DLC Campaign ID", value = "N/A" },
                new { label = "Current Provider Name", value = s.CurrentProvider ?? o.CurrentProviderHint ?? "" },
                new { label = "Current Provider Account Number", value = string.IsNullOrWhiteSpace(s.AccountNumber) ? "N/A" : s.AccountNumber },
                new { label = "Account Type", value = o.AccountType },
                new { label = "Authorized Name on the Account", value = s.AuthorizedName },
                new { label = "Billing Phone Number", value = s.BillingPhone },
                new { label = "End User Name", value = o.EndUserName },
                new { label = "Phone Service Address", value = s.ServiceAddressLine },
                new { label = "PIN", value = o.PinNotApplicable ? "N/A" : pin ?? (o.SensitivePurgedAt is null ? "" : "(deleted)") },
            },
            signer = s, o.SignerIp, o.SignatureName,
        });
    }

    private static async Task<IResult> PortalFile(Guid id, string kind, ContactConnectionDbContext master, IBlobStorage blobs, CancellationToken ct)
    {
        var o = await master.PortOrders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return o is null ? Results.NotFound() : await FileAsync(o, kind, blobs, ct);
    }

    /// <summary>Loads (or reloads) a port's state change, emails the requester, saves.</summary>
    private static async Task<IResult> ActAsync(Guid id, ContactConnectionDbContext master, IEmailService email, ILoggerFactory logs, CancellationToken ct,
        Func<PortOrder, (string Subject, string Text)?> act)
    {
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Results.NotFound();
        (string Subject, string Text)? notice;
        try { notice = act(o); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
        await master.SaveChangesAsync(ct);
        if (notice is { } n) await NotifyRequesterAsync(o, n.Subject, n.Text, email, logs.CreateLogger("Porting"), ct);
        return Results.Ok(Summary(o));
    }

    public record SubmittedRequest(string? OrderNumber);
    private static Task<IResult> Submitted(Guid id, SubmittedRequest req, HttpContext http, ContactConnectionDbContext master, IEmailService email, ILoggerFactory logs, CancellationToken ct) =>
        ActAsync(id, master, email, logs, ct, o =>
        {
            o.MarkSubmitted(req.OrderNumber ?? "", Who(http), DateTimeOffset.UtcNow);
            return ("submitted to the carrier", "The port has been submitted. The current phone company usually confirms a port date within a few business days.");
        });

    public record FocRequest(DateOnly? Date);

    /// <summary>The carrier confirmed the date: record it and load the numbers into the tenant's account now, so they work
    /// the moment the carrier switches.</summary>
    private static async Task<IResult> Foc(Guid id, FocRequest req, HttpContext http, ContactConnectionDbContext master, PortNumberLoader loader,
        IEmailService email, ILoggerFactory logs, CancellationToken ct)
    {
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Results.NotFound();
        if (req.Date is not { } d) return Results.BadRequest(new { error = "Pick the confirmed port date." });
        try { o.ConfirmFoc(d, Who(http), DateTimeOffset.UtcNow); }
        catch (InvalidOperationException e) { return Results.BadRequest(new { error = e.Message }); }
        await master.SaveChangesAsync(ct);
        await loader.LoadAsync(o, ct);
        var where = o.PreAssignCampaignId is null
            ? $"They're already in your Reserve, labelled <b>{H(o.Label)}</b> — assign them to campaigns any time before then."
            : $"They're already on the campaign you chose (labelled <b>{H(o.Label)}</b>), so they'll take calls as soon as the move happens.";
        await NotifyRequesterAsync(o, $"port date confirmed: {d:MMMM d, yyyy}",
            $"The numbers move on <b>{d:dddd, MMMM d, yyyy}</b>. {where} Keep service with your current phone company until then.",
            email, logs.CreateLogger("Porting"), ct);
        return Results.Ok(Summary(o));
    }

    private static Task<IResult> Complete(Guid id, HttpContext http, ContactConnectionDbContext master, IEmailService email, ILoggerFactory logs, CancellationToken ct) =>
        ActAsync(id, master, email, logs, ct, o =>
        {
            o.Complete(Who(http), DateTimeOffset.UtcNow);
            return ("completed", "The port is complete — the numbers are now with ContactConnection.");
        });

    public record MessageRequest(string? Message);
    private static async Task<IResult> Correction(Guid id, MessageRequest req, HttpContext http, ContactConnectionDbContext master, IEmailService email,
        IConfiguration config, ILoggerFactory logs, CancellationToken ct)
    {
        var o = await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return Results.NotFound();
        string token;
        try { token = o.RequestCorrection(req.Message ?? "", Who(http), DateTimeOffset.UtcNow); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException) { return Results.BadRequest(new { error = e.Message }); }
        await master.SaveChangesAsync(ct);
        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == o.TenantId, ct);
        var log = logs.CreateLogger("Porting");
        await SendSigningLinkAsync(o, token, tenant, email, config, log, ct);
        await NotifyRequesterAsync(o, "correction needed", $"The signer has been asked to correct: {H(o.CorrectionMessage)}", email, log, ct);
        return Results.Ok(Summary(o));
    }

    private static async Task<IResult> PortalCancel(Guid id, CancelRequest req, HttpContext http, ContactConnectionDbContext master, PortNumberLoader loader,
        IEmailService email, ILoggerFactory logs, CancellationToken ct)
    {
        var result = await ActAsync(id, master, email, logs, ct, o =>
        {
            o.Cancel(req.Reason ?? "", Who(http), DateTimeOffset.UtcNow);
            return ("cancelled", $"The port was cancelled{(string.IsNullOrWhiteSpace(req.Reason) ? "" : ": " + H(req.Reason))}." +
                (o.NumbersLoadedAt is null ? "" : " The numbers it had added to your account have been removed."));
        });
        // The numbers never arrived — take out the ones this port loaded.
        if (await master.PortOrders.FirstOrDefaultAsync(x => x.Id == id && x.Status == PortOrderStatus.Cancelled, ct) is { NumbersLoadedAt: not null } cancelled)
            await loader.UnloadAsync(cancelled, ct);
        return result;
    }

    private static Task<IResult> Note(Guid id, MessageRequest req, HttpContext http, ContactConnectionDbContext master, IEmailService email, ILoggerFactory logs, CancellationToken ct) =>
        ActAsync(id, master, email, logs, ct, o => { o.Note(req.Message ?? "", Who(http), DateTimeOffset.UtcNow); return null; });
}
