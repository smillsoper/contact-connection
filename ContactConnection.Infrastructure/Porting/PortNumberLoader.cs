using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using ContactConnection.Infrastructure.Telephony;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Porting;

/// <summary>
/// Puts a port's numbers into the tenant's account as soon as the port date is confirmed (S184) — labelled "Port P-…" and
/// in the Reserve, or straight onto the pre-assigned campaign with its flow overrides — so they work the moment the
/// carrier switches, instead of waiting for someone to notice. Calls can't reach them before then anyway. Goes through
/// NumberOwnership, the same path as adding numbers by hand, so the platform routing table stays true.
/// </summary>
public sealed class PortNumberLoader(ContactConnectionDbContext master, ITenantDbContextFactory tenantDbs)
{
    public async Task LoadAsync(PortOrder order, CancellationToken ct)
    {
        if (order.NumbersLoadedAt is not null) return;
        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == order.TenantId, ct);
        await using var db = tenantDbs.Create(tenant.SchemaName);

        // The pre-assigned campaign (and flows) only if they still exist; otherwise the Reserve.
        var campaign = order.PreAssignCampaignId is { } cid
            ? await db.Campaigns.AsNoTracking().Where(c => c.Id == cid && c.Status != CampaignStatus.Inactive).Select(c => new { c.Id, c.Name }).FirstOrDefaultAsync(ct)
            : null;
        var flowIds = new[] { order.PreAssignFlowId, order.PreAssignTelephonyFlowId }.OfType<Guid>().ToList();
        var flows = await db.Flows.AsNoTracking().Where(f => flowIds.Contains(f.Id) && f.IsActive).Select(f => f.Id).ToListAsync(ct);

        var ownership = new NumberOwnership(master);
        var loaded = 0;
        var skipped = new List<string>();
        foreach (var n in order.Numbers)
        {
            var forms = PhoneNumber.Forms(n);
            if (await db.PhoneNumbers.AnyAsync(p => forms.Contains(p.Number) && !(!p.IsActive && p.CampaignId == null), ct)
                || await ownership.ConflictAsync(order.TenantId, n, false, ct) is not null)
            {
                skipped.Add(n);
                continue;
            }
            var pn = PhoneNumber.Create(order.TenantId, campaign?.Id, n, order.Label);
            if (campaign is not null && order.PreAssignFlowId is { } f && flows.Contains(f)) pn.AssignFlow(f);
            if (campaign is not null && order.PreAssignTelephonyFlowId is { } tf && flows.Contains(tf)) pn.AssignTelephonyFlow(tf);
            db.PhoneNumbers.Add(pn);
            await ownership.ApplyAsync(pn, ct);
            loaded++;
        }
        await db.SaveChangesAsync(ct);
        order.MarkNumbersLoaded(loaded, skipped, campaign is null ? "Reserve" : $"campaign {campaign.Name}", DateTimeOffset.UtcNow);
        await master.SaveChangesAsync(ct);   // routing rows + the order, together
    }

    /// <summary>A port cancelled after loading: the numbers never arrived, so the ones this port added come back out.</summary>
    public async Task UnloadAsync(PortOrder order, CancellationToken ct)
    {
        if (order.NumbersLoadedAt is not { } loadedAt) return;
        var tenant = await master.Tenants.AsNoTracking().FirstAsync(t => t.Id == order.TenantId, ct);
        await using var db = tenantDbs.Create(tenant.SchemaName);
        var forms = order.Numbers.SelectMany(PhoneNumber.Forms).ToList();
        var added = await db.PhoneNumbers.Where(p => forms.Contains(p.Number) && p.CreatedAt >= loadedAt.AddSeconds(-5)).ToListAsync(ct);
        var ownership = new NumberOwnership(master);
        foreach (var pn in added)
        {
            await ownership.ForgetAsync(pn, ct);
            db.PhoneNumbers.Remove(pn);
        }
        await db.SaveChangesAsync(ct);
        order.MarkNumbersUnloaded(added.Count, DateTimeOffset.UtcNow);
        await master.SaveChangesAsync(ct);
    }
}
