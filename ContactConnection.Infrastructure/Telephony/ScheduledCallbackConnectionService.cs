using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ContactConnection.Infrastructure.Telephony;

public class ScheduledCallbackConnectionService(
    ITenantDbContextFactory dbFactory,
    ICallStateHistoryRecorder callStateRecorder,
    ILogger<ScheduledCallbackConnectionService> logger) : IScheduledCallbackConnectionService
{
    public async Task<bool> MarkConnectedAsync(
        string tenantSchemaName, Guid tenantId, Guid callbackId, Guid connectedCallRecordId,
        CancellationToken ct = default)
    {
        await using var db = dbFactory.Create(tenantSchemaName);

        var callback = await db.ScheduledCallbacks.FirstOrDefaultAsync(c => c.Id == callbackId, ct);
        if (callback is null)
        {
            logger.LogWarning("ScheduledCallbackConnectionService: callback {CallbackId} not found (connected)", callbackId);
            return false;
        }
        if (callback.Status != ScheduledCallbackStatus.Attempted)
        {
            logger.LogInformation(
                "ScheduledCallbackConnectionService: callback {CallbackId} is '{Status}', not 'attempted' — ignoring connect",
                callbackId, callback.Status);
            return false;
        }

        callback.LinkConnectedCallRecord(connectedCallRecordId);
        callback.MarkCompleted();
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "ScheduledCallbackConnectionService: callback {CallbackId} completed — connected call record {RecordId}",
            callbackId, connectedCallRecordId);
        return true;
    }

    public async Task<bool> MarkNoAnswerAsync(
        string tenantSchemaName, Guid tenantId, Guid callbackId, string? cause,
        CancellationToken ct = default)
    {
        await using var db = dbFactory.Create(tenantSchemaName);

        var callback = await db.ScheduledCallbacks.FirstOrDefaultAsync(c => c.Id == callbackId, ct);
        if (callback is null)
        {
            logger.LogWarning("ScheduledCallbackConnectionService: callback {CallbackId} not found (no-answer)", callbackId);
            return false;
        }
        if (callback.Status != ScheduledCallbackStatus.Attempted)
        {
            logger.LogInformation(
                "ScheduledCallbackConnectionService: callback {CallbackId} is '{Status}', not 'attempted' — ignoring no-answer",
                callbackId, callback.Status);
            return false;
        }

        var abandoned = callback.MarkNoAnswer(
            string.IsNullOrWhiteSpace(cause) ? "Callback leg did not connect." : $"No answer ({cause}).");
        await db.SaveChangesAsync(ct);

        if (abandoned)
        {
            await callStateRecorder.RecordAsync(
                tenantId, tenantSchemaName, callback.CallRecordId,
                CallHistoryState.Abandoned, callback.CampaignId, agentId: null,
                detail: $"Scheduled callback abandoned after {callback.AttemptCount} attempt(s)",
                abandonType: CallAbandonType.CallbackAbandon, ct: ct);

            logger.LogInformation(
                "ScheduledCallbackConnectionService: callback {CallbackId} abandoned after {Attempts} attempt(s)",
                callbackId, callback.AttemptCount);
        }
        else
        {
            logger.LogInformation(
                "ScheduledCallbackConnectionService: callback {CallbackId} no-answer on attempt {Attempt}/{Max} — rescheduled",
                callbackId, callback.AttemptCount, callback.MaxAttempts);
        }

        return abandoned;
    }
}
