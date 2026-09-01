using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.Repositories;

public class VoicemailRepository : IVoicemailRepository
{
    private readonly ScopedTenantDbContextFactory _factory;
    private TenantDbContext? _db;
    private TenantDbContext Db => _db ??= _factory.Create();

    public VoicemailRepository(ScopedTenantDbContextFactory factory) => _factory = factory;

    public Task<Voicemail?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        Db.Voicemails.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<Voicemail>> ListByCallRecordAsync(Guid callRecordId, CancellationToken ct = default) =>
        await Db.Voicemails
            .Where(v => v.CallRecordId == callRecordId)
            .OrderByDescending(v => v.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Voicemail>> ListByCampaignAsync(
        Guid campaignId, string? status, int limit, CancellationToken ct = default)
    {
        var q = Db.Voicemails.Where(v => v.CampaignId == campaignId);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(v => v.Status == status);

        return await q.OrderByDescending(v => v.CreatedAt).Take(limit).ToListAsync(ct);
    }

    public async Task AddAsync(Voicemail voicemail, CancellationToken ct = default) =>
        await Db.Voicemails.AddAsync(voicemail, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) =>
        Db.SaveChangesAsync(ct);
}
