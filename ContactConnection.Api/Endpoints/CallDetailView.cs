using System.Text.Json;
using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Application.Interfaces.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// A call's full details for dashboard viewers (S181) — the records widget's detail view, on internal and client dashboards.
/// Mirrors the Call Records page's read side: the call, customer, addresses, media, custom fields, and per interaction its
/// confirmed summary, order, cart, payments and the values the script captured. Deliberately left out: the audit history,
/// card-on-file state, API request/response payloads, gateway transaction ids and auth codes, and every action.
/// </summary>
public static class CallDetailView
{
    public sealed class Deps(
        ICallRecordRepository callRecords, IFlowSessionRepository sessions, IFlowRepository flows, IFlowEngine engine,
        IPaymentTransactionRepository payments, ICustomFieldService customFields, ICustomFieldValueRepository customFieldValues,
        ICustomFieldDefinitionRepository customFieldDefinitions, Infrastructure.Data.ScopedTenantDbContextFactory dbFactory)
    {
        public ICallRecordRepository CallRecords { get; } = callRecords;
        public IFlowSessionRepository Sessions { get; } = sessions;
        public IFlowRepository Flows { get; } = flows;
        public IFlowEngine Engine { get; } = engine;
        public IPaymentTransactionRepository Payments { get; } = payments;
        public ICustomFieldService CustomFields { get; } = customFields;
        public ICustomFieldValueRepository CustomFieldValues { get; } = customFieldValues;
        public ICustomFieldDefinitionRepository CustomFieldDefinitions { get; } = customFieldDefinitions;
        public ScopedTenantDbContextFactory DbFactory { get; } = dbFactory;
    }

    /// <summary>Flow variables that are plumbing, not captured data: internal (_x), and large structured blobs (API responses).</summary>
    internal static bool IsCapturedValue(string key, string? value) =>
        !key.StartsWith('_') && !key.Contains("._") && value is not null
        && !(value.Length > 300 && (value.TrimStart().StartsWith('{') || value.TrimStart().StartsWith('[')));

    /// <returns>The detail, and the recording's state (available / purged / none) for the caller's playback decision.</returns>
    public static async Task<(object Detail, string RecordingStatus)?> BuildAsync(Guid id, Deps d, string timeZone, CancellationToken ct)
    {
        var r = await d.CallRecords.GetByIdWithInteractionsAsync(id, ct);
        if (r is null || r.RunMode != CallRunMode.Production) return null;

        await using var db = d.DbFactory.Create();
        var campaignIds = r.Interactions.Select(i => i.CampaignId).OfType<Guid>().Append(r.CampaignId).Distinct().ToList();
        var campaigns = await db.Campaigns.AsNoTracking().Where(c => campaignIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);
        var clientName = r.ClientId == Guid.Empty ? null : await db.Clients.AsNoTracking().Where(c => c.Id == r.ClientId).Select(c => c.Name).FirstOrDefaultAsync(ct);
        var agentIds = r.Interactions.Select(i => i.AgentId).OfType<Guid>().Distinct().ToList();
        var agents = await db.Agents.AsNoTracking().Where(a => agentIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.FirstName + " " + a.LastName, ct);
        var categories = await db.Dispositions.AsNoTracking().Join(db.DispositionCategories.AsNoTracking(), x => x.CategoryId, c => c.Id, (x, c) => new { x.Id, c.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var summaries = (await db.CallSummaries.AsNoTracking().Where(s => s.CallRecordId == id && s.Status == CallSummaryStatus.Confirmed).ToListAsync(ct))
            .GroupBy(s => s.InteractionId).ToDictionary(g => g.Key ?? Guid.Empty, g => g.OrderByDescending(s => s.CreatedAt).First());
        var txns = await d.Payments.GetByCallRecordAsync(id, ct);
        var sessionRows = await d.Sessions.GetByCallRecordAsync(id, ct);

        var captured = new List<(Guid? InteractionId, string? FlowName, Dictionary<string, string> Values)>();
        foreach (var s in sessionRows.OrderBy(s => s.StartedAt))
        {
            var snap = await d.Engine.GetSessionSnapshotAsync(s.Id, ct);
            if (snap is null) continue;
            // API and payment steps' outputs (order API responses, gateway results with auth codes / transaction ids) are
            // system data, not captured answers — the order and payments sections show what matters from them.
            var outputs = snap.ApiCalls.Select(a => a.OutputVariable).Where(o => !string.IsNullOrEmpty(o)).Select(o => o!).ToList();
            bool IsOutput(string key) => outputs.Any(o => key.Equals(o, StringComparison.OrdinalIgnoreCase)
                || key.StartsWith(o + ".", StringComparison.OrdinalIgnoreCase));
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (k, v) in snap.Inputs) if (IsCapturedValue(k, v) && !IsOutput(k)) values[k] = v;
            foreach (var (k, v) in snap.FlowVars) if (IsCapturedValue(k, v) && !IsOutput(k)) values.TryAdd(k, v);
            var flow = await d.Flows.GetByIdAsync(s.FlowId, ct);
            captured.Add((s.InteractionId, flow?.Name, values));
        }

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); } catch (Exception) { zone = TimeZoneInfo.Utc; }
        string? At(DateTimeOffset? t) => t is { } v ? TimeZoneInfo.ConvertTime(v, zone).ToString("yyyy-MM-dd h:mm:ss tt") : null;
        object? Address(Domain.ValueObjects.AddressData? a) => a is null ? null : new
        {
            name = string.Join(" ", new[] { a.FirstName, a.MiddleInitial, a.LastName }.Where(p => !string.IsNullOrWhiteSpace(p))),
            a.Company,
            street = string.Join(" ", new[] { a.Prefix, a.Street }.Where(p => !string.IsNullOrWhiteSpace(p))),
            unit = string.Join(" ", new[] { a.UnitPrefix, a.Unit }.Where(p => !string.IsNullOrWhiteSpace(p))),
            a.City, a.State, zip = string.IsNullOrEmpty(a.Zip4) ? a.Zip : $"{a.Zip}-{a.Zip4}", a.Country, verified = a.IsVerified,
        };

        var ordered = r.Interactions.OrderBy(i => i.StartedAt).ThenBy(i => i.InteractionNumber).ToList();
        var recordingStatus = !r.RecordingRetained ? "purged" : string.IsNullOrEmpty(r.RecordingUrl) ? "none" : "available";
        return (new
        {
            r.Id,
            startedAt = At(r.CallStartAt ?? r.CreatedAt),
            endedAt = At(r.CallEndAt),
            handleTimeSeconds = r.HandleTimeSeconds,
            r.Source,
            r.OverallStatus,
            clientName,
            campaignName = campaigns.GetValueOrDefault(r.CampaignId),
            r.CallerId,
            r.Dnis,
            media = r.MediaAttribution is { } m ? new { m.Agency, m.Station, m.MarketType, m.MediaType, m.AdType, m.PhoneNumber, m.Fields } : null,
            compoundDisposition = r.CompoundDisposition,
            contact = new { r.FirstName, r.LastName, r.Email, r.Phone, r.BillingPhone, r.ShippingPhone, r.AccountNumber, r.ClientNumber },
            billing = Address(r.Addresses?.Billing),
            shipping = Address(r.Addresses?.Shipping),
            customFields = (await CallReviewEndpoints.CustomFieldViewsAsync(id, d.CustomFields, d.CustomFieldValues, d.CustomFieldDefinitions, ct))
                .Select(f => JsonSerializer.SerializeToElement(f, JsonSerializerOptions.Web))
                .Where(f => f.TryGetProperty("value", out var v) && v.ValueKind != JsonValueKind.Null)
                .Select(f => new { label = f.GetProperty("displayLabel").GetString(), value = f.GetProperty("value").GetString() }),
            interactions = ordered.Select((i, n) =>
            {
                var summary = summaries.GetValueOrDefault(i.Id) ?? (ordered.Count == 1 ? summaries.GetValueOrDefault(Guid.Empty) : null);
                return new
                {
                    number = n + 1,
                    campaignName = i.CampaignId is { } c ? campaigns.GetValueOrDefault(c) : null,
                    agentName = i.AgentId is { } a ? agents.GetValueOrDefault(a) : null,
                    i.Disposition,
                    category = i.DispositionId is { } dsp ? categories.GetValueOrDefault(dsp) : null,
                    i.Status,
                    startedAt = At(i.StartedAt),
                    completedAt = At(i.CompletedAt),
                    i.OrderNumber,
                    orderSubmittedAt = At(i.OrderSubmittedAt),
                    i.PaymentStatus,
                    i.Cart,
                    summary = summary is null ? null : new { summary.Summary, summary.ReasonForCall, summary.Outcome, summary.FollowUp },
                    payments = txns.Where(t => t.InteractionId == i.Id || (t.InteractionId == null && n == 0)).Select(t => new
                    {
                        t.TransactionType, t.Amount, t.Status, t.CardType, t.CardLast4, reason = t.ResponseReasonText, at = At(t.CreatedAt),
                        voided = t.VoidedAt != null,
                    }),
                    fields = string.IsNullOrEmpty(i.CustomFields) ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(i.CustomFields),
                    captured = captured.Where(c => c.InteractionId == i.Id || (c.InteractionId == null && n == 0))
                        .Select(c => new { flowName = c.FlowName, values = c.Values }),
                };
            }),
            recordingStatus,
        }, recordingStatus);
    }
}
