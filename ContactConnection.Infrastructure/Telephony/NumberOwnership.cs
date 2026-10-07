using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Telephony;

/// <summary>
/// Keeps the platform-wide routing table (public.phone_number_routings) true to each tenant's numbers (S182), and decides
/// who may hold a number. The same number may exist in several tenants' schemas (history is never deleted), but only one
/// routing row exists per number, owned by one tenant:
///
///   • a tenant may add / move / activate a number it already owns, or one that is unknown to the platform;
///   • another tenant's number may be taken only once that tenant RELEASED it (deactivated while in Reserve) — the old
///     tenant's row stays, inactive, for its history;
///   • a released number of this tenant never touches a routing row someone else now owns.
///
/// Calls route only to an active number on a campaign; Reserve, inactive and released numbers are rejected (SIP 404).
/// </summary>
public sealed class NumberOwnership(ContactConnectionDbContext platform)
{
    public const string InUseElsewhere = "is in use by another account — contact ContactConnection support.";

    private Task<PhoneNumberRouting?> RowAsync(string number, CancellationToken ct)
    {
        var forms = PhoneNumber.Forms(number);
        return platform.PhoneNumberRoutings.FirstOrDefaultAsync(r => forms.Contains(r.Number), ct);
    }

    /// <summary>Why this tenant can't hold the number in this state, or null. Changes nothing.</summary>
    public async Task<string?> ConflictAsync(Guid tenantId, string number, bool releasedHere, CancellationToken ct = default)
    {
        if (releasedHere) return null;   // letting go never conflicts
        var row = await RowAsync(number, ct);
        return row is null || row.TenantId == tenantId || row.IsReleased ? null : $"{PhoneNumber.Normalize(number)} {InUseElsewhere}";
    }

    /// <summary>Mirrors the number's state into the routing table (call <see cref="ConflictAsync"/> first). Not saved —
    /// call <see cref="SaveAsync"/>.</summary>
    public async Task ApplyAsync(PhoneNumber pn, CancellationToken ct = default)
    {
        var row = await RowAsync(pn.Number, ct);
        if (row is null)
        {
            if (pn.IsReleased) return;   // nothing to route, nothing to own
            var created = PhoneNumberRouting.Create(pn.Number, pn.TenantId, pn.CampaignId);
            if (!pn.IsActive) created.Deactivate();
            platform.PhoneNumberRoutings.Add(created);
            return;
        }
        if (row.TenantId == pn.TenantId)
        {
            row.Update(pn.CampaignId);
            if (pn.IsActive) row.Activate(); else row.Deactivate();
            return;
        }
        if (pn.IsReleased) return;                                   // someone else's now — leave it alone
        if (row.IsReleased) row.TransferTo(pn.TenantId, pn.CampaignId, pn.IsActive);
        else throw new InvalidOperationException($"{pn.Number} {InUseElsewhere}");
    }

    /// <summary>A never-used number deleted from this tenant: drop its routing row if this tenant owns it. Not saved.</summary>
    public async Task ForgetAsync(PhoneNumber pn, CancellationToken ct = default)
    {
        var row = await RowAsync(pn.Number, ct);
        if (row is not null && row.TenantId == pn.TenantId) platform.PhoneNumberRoutings.Remove(row);
    }

    public Task SaveAsync(CancellationToken ct = default) => platform.SaveChangesAsync(ct);
}
