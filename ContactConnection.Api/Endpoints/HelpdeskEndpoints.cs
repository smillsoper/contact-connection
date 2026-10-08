using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Agent help desks (S184). An admin (helpdesk.manage) builds help desks — topics with formatted text, embedded
/// images, attached files and links — and assigns each to campaigns. On a call the agent opens every active help desk
/// for the call's campaign(s), a tab each. Help desks are internal reference material: anyone signed in to the tenant
/// can read an active one.
/// </summary>
public static class HelpdeskEndpoints
{
    public static IEndpointRouteBuilder MapHelpdeskEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin/helpdesks").RequireAuthorization("HelpdeskManage");
        admin.MapGet("", AdminList);
        admin.MapGet("campaigns", AdminCampaigns);
        admin.MapPost("", Create);
        admin.MapGet("{id:guid}", AdminGet);
        admin.MapPut("{id:guid}", Update);
        admin.MapDelete("{id:guid}", Delete);
        admin.MapPost("{id:guid}/topics", CreateTopic);
        admin.MapPut("{id:guid}/topics/{topicId:guid}", UpdateTopic);
        admin.MapDelete("{id:guid}/topics/{topicId:guid}", DeleteTopic);
        admin.MapPut("{id:guid}/topic-order", Reorder);
        admin.MapPost("{id:guid}/files", Upload).DisableAntiforgery();

        var g = app.MapGroup("/api/v1/helpdesks").RequireAuthorization();
        g.MapGet("for-call/{callRecordId:guid}", ForCall);
        g.MapGet("files/{fileId:guid}", GetFile);
        return app;
    }

    private static Guid? Me(HttpContext http) => Guid.TryParse(http.User.FindFirst("sub")?.Value, out var id) ? id : null;

    private static bool IsManager(HttpContext http) =>
        (http.User.FindFirst("permissions")?.Value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Contains(Permission.HelpdeskManage);

    // ── Shapes ───────────────────────────────────────────────────────────────────────────────────────────────────

    private static object FileDto(HelpdeskFile f) => new { f.Id, name = f.FileName, f.ContentType, f.SizeBytes, f.IsImage };

    private static object TopicDto(HelpdeskTopic t, IReadOnlyDictionary<Guid, HelpdeskFile> files) => new
    {
        t.Id, t.Title, t.Html, t.SortOrder, t.UpdatedAt, t.UpdatedByName,
        attachments = t.AttachmentIds.Select(files.GetValueOrDefault).OfType<HelpdeskFile>().Select(FileDto),
    };

    private static async Task<object> FullDto(TenantDbContext db, Helpdesk h, CancellationToken ct)
    {
        var topics = await db.HelpdeskTopics.AsNoTracking().Where(t => t.HelpdeskId == h.Id).OrderBy(t => t.SortOrder).ThenBy(t => t.CreatedAt).ToListAsync(ct);
        var files = await db.HelpdeskFiles.AsNoTracking().Where(f => f.HelpdeskId == h.Id && !f.IsImage).ToDictionaryAsync(f => f.Id, ct);
        return new { h.Id, h.Name, h.Description, h.CampaignIds, h.IsActive, h.UpdatedAt, topics = topics.Select(t => TopicDto(t, files)) };
    }

    // ── Admin: help desks ────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<IResult> AdminList(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var desks = await db.Helpdesks.AsNoTracking().OrderBy(h => h.Name).ToListAsync(ct);
        var counts = await db.HelpdeskTopics.AsNoTracking().GroupBy(t => t.HelpdeskId).Select(g => new { g.Key, n = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.n, ct);
        return Results.Ok(desks.Select(h => new { h.Id, h.Name, h.Description, h.CampaignIds, h.IsActive, h.UpdatedAt, topicCount = counts.GetValueOrDefault(h.Id) }));
    }

    /// <summary>Campaigns for the picker — a help desk manager may not hold the campaigns permission.</summary>
    private static async Task<IResult> AdminCampaigns(ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var clients = await db.Clients.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var campaigns = await db.Campaigns.AsNoTracking().OrderBy(c => c.Name).Select(c => new { c.Id, c.Name, c.ClientId, c.Status }).ToListAsync(ct);
        return Results.Ok(campaigns.Select(c => new { c.Id, c.Name, client = clients.GetValueOrDefault(c.ClientId) ?? "", c.Status })
            .OrderBy(c => c.client).ThenBy(c => c.Name));
    }

    public record HelpdeskRequest(string? Name, string? Description, List<Guid>? CampaignIds, bool? IsActive);

    private static async Task<IResult> Create(HelpdeskRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        Helpdesk h;
        try { h = Helpdesk.Create(tenant.Id, req.Name ?? "", req.Description, req.CampaignIds ?? []); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        await using var db = dbf.Create();
        db.Helpdesks.Add(h);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await FullDto(db, h, ct));
    }

    private static async Task<IResult> AdminGet(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var h = await db.Helpdesks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return h is null ? Results.NotFound() : Results.Ok(await FullDto(db, h, ct));
    }

    private static async Task<IResult> Update(Guid id, HelpdeskRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var h = await db.Helpdesks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return Results.NotFound();
        try { h.Update(req.Name ?? "", req.Description, req.CampaignIds ?? [], req.IsActive ?? h.IsActive); }
        catch (ArgumentException e) { return Results.BadRequest(new { error = e.Message }); }
        await db.SaveChangesAsync(ct);
        return Results.Ok(await FullDto(db, h, ct));
    }

    private static async Task<IResult> Delete(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, IBlobStorage blobs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var h = await db.Helpdesks.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (h is null) return Results.NotFound();
        db.HelpdeskTopics.RemoveRange(db.HelpdeskTopics.Where(t => t.HelpdeskId == id));
        db.HelpdeskFiles.RemoveRange(db.HelpdeskFiles.Where(f => f.HelpdeskId == id));
        db.Helpdesks.Remove(h);
        await db.SaveChangesAsync(ct);
        await blobs.DeletePrefixAsync($"helpdesk/{tenant.Id}/{id}/", ct);
        return Results.NoContent();
    }

    // ── Admin: topics ────────────────────────────────────────────────────────────────────────────────────────────

    public record TopicRequest(string? Title, string? Html, List<Guid>? AttachmentIds);

    private static async Task<IResult> CreateTopic(Guid id, TopicRequest req, HttpContext http, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (!await db.Helpdesks.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
        var next = (await db.HelpdeskTopics.Where(t => t.HelpdeskId == id).MaxAsync(t => (int?)t.SortOrder, ct) ?? -1) + 1;
        var topic = HelpdeskTopic.Create(id, next);
        if (await ApplyAsync(db, topic, req, http, ct) is { } error) return Results.BadRequest(new { error });
        db.HelpdeskTopics.Add(topic);
        await db.SaveChangesAsync(ct);
        return Results.Ok(await TopicResult(db, topic, ct));
    }

    private static async Task<IResult> UpdateTopic(Guid id, Guid topicId, TopicRequest req, HttpContext http, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var topic = await db.HelpdeskTopics.FirstOrDefaultAsync(t => t.Id == topicId && t.HelpdeskId == id, ct);
        if (topic is null) return Results.NotFound();
        if (await ApplyAsync(db, topic, req, http, ct) is { } error) return Results.BadRequest(new { error });
        await db.SaveChangesAsync(ct);
        return Results.Ok(await TopicResult(db, topic, ct));
    }

    /// <summary>Cleans the content and checks every embedded image and attachment is a file of this help desk.</summary>
    private static async Task<string?> ApplyAsync(TenantDbContext db, HelpdeskTopic topic, TopicRequest req, HttpContext http, CancellationToken ct)
    {
        var clean = HelpdeskRichText.Clean(req.Html ?? "");
        var attachments = (req.AttachmentIds ?? []).Distinct().ToList();
        var wanted = clean.ImageIds.Concat(attachments).Distinct().ToList();
        var files = await db.HelpdeskFiles.AsNoTracking().Where(f => wanted.Contains(f.Id) && f.HelpdeskId == topic.HelpdeskId).ToListAsync(ct);
        if (clean.ImageIds.Any(i => !files.Any(f => f.Id == i && f.IsImage))) return "An image in this topic is no longer available — paste it again.";
        if (attachments.Any(a => !files.Any(f => f.Id == a && !f.IsImage))) return "An attached file is no longer available — attach it again.";
        var me = Me(http);
        var name = me is { } m ? await db.Agents.Where(a => a.Id == m).Select(a => (a.FirstName + " " + a.LastName).Trim()).FirstOrDefaultAsync(ct) : null;
        try { topic.Update(req.Title ?? "", clean.Html, clean.Text, attachments, name ?? "Admin"); }
        catch (ArgumentException e) { return e.Message; }
        return null;
    }

    private static async Task<object> TopicResult(TenantDbContext db, HelpdeskTopic t, CancellationToken ct)
    {
        var files = await db.HelpdeskFiles.AsNoTracking().Where(f => t.AttachmentIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);
        return TopicDto(t, files);
    }

    private static async Task<IResult> DeleteTopic(Guid id, Guid topicId, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var topic = await db.HelpdeskTopics.FirstOrDefaultAsync(t => t.Id == topicId && t.HelpdeskId == id, ct);
        if (topic is null) return Results.NotFound();
        db.HelpdeskTopics.Remove(topic);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public record ReorderRequest(List<Guid>? TopicIds);

    private static async Task<IResult> Reorder(Guid id, ReorderRequest req, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var topics = await db.HelpdeskTopics.Where(t => t.HelpdeskId == id).ToListAsync(ct);
        var order = req.TopicIds ?? [];
        foreach (var t in topics)
        {
            var i = order.IndexOf(t.Id);
            t.MoveTo(i < 0 ? order.Count + t.SortOrder : i);
        }
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    // ── Admin: files ─────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>An embedded image (kind=image, default: PNG/JPEG/GIF/WebP checked by content, up to 5 MB) or an
    /// attachment (kind=file: up to 25 MB, no programs or scripts, always downloaded). Raw body.</summary>
    private static async Task<IResult> Upload(Guid id, string? kind, string? name, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf,
        IBlobStorage blobs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant || Me(http) is not { } me) return Results.Unauthorized();
        await using var db = dbf.Create();
        if (!await db.Helpdesks.AnyAsync(h => h.Id == id, ct)) return Results.NotFound();
        var asFile = kind == "file";
        var fileName = ChatFile.CleanName(name);
        if (asFile && ChatFile.BlockedExtensions.Contains(Path.GetExtension(fileName)))
            return Results.BadRequest(new { error = "Programs and scripts can't be attached." });
        var max = asFile ? HelpdeskFile.MaxFileBytes : HelpdeskFile.MaxImageBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await http.Request.Body.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max) return Results.BadRequest(new { error = asFile ? "Files can be up to 25 MB." : "Images can be up to 5 MB." });
        }
        if (buffer.Length == 0) return Results.BadRequest(new { error = "Nothing was uploaded." });
        string type;
        if (asFile)
        {
            var declared = (http.Request.ContentType ?? "").Split(';')[0].Trim();
            type = declared.Length is > 0 and <= 120 ? declared : "application/octet-stream";
        }
        else
        {
            var sniffed = ChatFile.SniffImageType(buffer.GetBuffer().AsSpan(0, (int)Math.Min(16, buffer.Length)));
            if (sniffed is null) return Results.BadRequest(new { error = "Only PNG, JPEG, GIF and WebP images can be embedded." });
            type = sniffed;
        }
        var file = HelpdeskFile.Create(tenant.Id, id, !asFile, fileName, type, buffer.Length, me);
        buffer.Position = 0;
        await blobs.PutAsync(file.StorageKey, buffer, type, ct);
        db.HelpdeskFiles.Add(file);
        await db.SaveChangesAsync(ct);
        return Results.Ok(FileDto(file));
    }

    // ── Agents ───────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The active help desks for every campaign on this call — the current campaign's first (a transferred
    /// call also keeps the earlier campaign's help desks).</summary>
    private static async Task<IResult> ForCall(Guid callRecordId, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var record = await db.CallRecords.AsNoTracking().Where(r => r.Id == callRecordId).Select(r => new { r.CampaignId }).FirstOrDefaultAsync(ct);
        if (record is null) return Results.Ok(Array.Empty<object>());
        var interactionCampaigns = await db.CallInteractions.AsNoTracking().Where(i => i.CallRecordId == callRecordId && i.CampaignId != null)
            .OrderByDescending(i => i.StartedAt).Select(i => i.CampaignId!.Value).ToListAsync(ct);
        var campaigns = interactionCampaigns.Append(record.CampaignId).Where(c => c != Guid.Empty).Distinct().ToList();
        if (campaigns.Count == 0) return Results.Ok(Array.Empty<object>());

        var desks = await db.Helpdesks.AsNoTracking().Where(h => h.IsActive && h.CampaignIds.Any(c => campaigns.Contains(c))).ToListAsync(ct);
        var ordered = desks.OrderBy(h => campaigns.FindIndex(c => h.CampaignIds.Contains(c))).ThenBy(h => h.Name).ToList();
        var result = new List<object>();
        foreach (var h in ordered) result.Add(await FullDto(db, h, ct));
        return Results.Ok(result);
    }

    /// <summary>An embedded image or attachment. Managers can read any; everyone else only files of active help desks.</summary>
    private static async Task<IResult> GetFile(Guid fileId, HttpContext http, TenantContext tc, ScopedTenantDbContextFactory dbf, IBlobStorage blobs, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var file = await db.HelpdeskFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fileId && f.TenantId == tenant.Id, ct);
        if (file is null) return Results.NotFound();
        if (!IsManager(http) && !await db.Helpdesks.AnyAsync(h => h.Id == file.HelpdeskId && h.IsActive, ct)) return Results.NotFound();
        var stream = await blobs.OpenReadAsync(file.StorageKey, ct);
        if (stream is null) return Results.NotFound();
        http.Response.Headers["X-Content-Type-Options"] = "nosniff";
        http.Response.Headers["Cache-Control"] = "private, max-age=86400";
        // Attachments always download as plain bytes — an uploaded .html can never open as a page.
        return file.IsImage ? Results.Stream(stream, file.ContentType) : Results.File(stream, "application/octet-stream", file.FileName);
    }
}
