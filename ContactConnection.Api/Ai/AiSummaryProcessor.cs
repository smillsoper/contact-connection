using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Ai;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Ai;

/// <summary>
/// Generates the automatic wrap-up summary for calls whose script just finished (AI step c, S171) — off the
/// agent's request path. Skips calls whose campaign hasn't opted in, calls that already have a summary, and
/// everything when no API key is configured. Saves the result as a <i>suggestion</i> (a person still
/// confirms it) and tells the agent's portal it's ready. A failure is logged and dropped: the agent can
/// still press Generate, or write the summary themselves.
/// </summary>
public sealed class AiSummaryProcessor(
    AiSummaryQueue queue, IServiceScopeFactory scopeFactory, ILogger<AiSummaryProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.ReadAllAsync(stoppingToken))
        {
            try { await ProcessAsync(item, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Automatic AI summary failed for call {CallRecordId}", item.CallRecordId); }
        }
    }

    private async Task ProcessAsync(AiSummaryQueue.Item item, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var services = scope.ServiceProvider;
        if (!services.GetRequiredService<AnthropicClient>().IsConfigured) return;

        var tenant = await services.GetRequiredService<ContactConnectionDbContext>().Tenants.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == item.TenantId, ct);
        if (tenant is null) return;
        services.GetRequiredService<TenantContext>().Current = tenant;

        await using var db = services.GetRequiredService<ScopedTenantDbContextFactory>().Create();
        var campaignId = await db.CallRecords.AsNoTracking().Where(r => r.Id == item.CallRecordId).Select(r => r.CampaignId).FirstOrDefaultAsync(ct);
        var enabled = await db.Campaigns.AsNoTracking().Where(c => c.Id == campaignId).Select(c => c.AiSummaryEnabled).FirstOrDefaultAsync(ct);
        if (!enabled) return;
        if (await db.CallSummaries.AnyAsync(s => s.CallRecordId == item.CallRecordId
                && (s.Status == CallSummaryStatus.Suggested || s.Status == CallSummaryStatus.Confirmed), ct)) return;

        var result = await services.GetRequiredService<CallSummarizer>().SummarizeAsync(db, item.CallRecordId, ct);
        if (result is null) return;
        var s = result.Summary;
        var u = result.Usage;
        db.CallSummaries.Add(CallSummary.Suggest(tenant.Id, item.CallRecordId, s.Text, s.ReasonForCall, s.Outcome,
            s.SuggestedDisposition, s.DispositionValid, s.Confidence, s.FollowUp, s.IsTestCall, result.PossibleTestCall,
            u.Model, u.InputTokens, u.OutputTokens, u.EstimatedCostUsd, u.ElapsedMs, "Automatic (script finished)"));
        await db.SaveChangesAsync(ct);

        if (item.AgentId is { } agentId)
            await services.GetRequiredService<IFlowNotifier>().PushAiSummaryReadyAsync(agentId, item.CallRecordId, ct);
        logger.LogInformation("Automatic AI summary ready for call {CallRecordId} ({Tokens} tokens, ${Cost})",
            item.CallRecordId, u.InputTokens + u.OutputTokens, u.EstimatedCostUsd);
    }
}
