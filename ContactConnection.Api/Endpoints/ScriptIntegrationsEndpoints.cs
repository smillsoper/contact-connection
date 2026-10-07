using System.Text.Json;
using System.Text.Json.Nodes;
using ContactConnection.Application.Services;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// The integrations a script reaches (S181) — for the designer sandbox launch, where each one can run in production,
/// sandbox or (client APIs) simulated. Walks the script and every script it runs or hands off to. Platform lookups
/// (address / ZIP — portal-scope API steps) always run for real and aren't listed.
/// </summary>
public static class ScriptIntegrationsEndpoints
{
    public sealed record ScriptIntegration(string Key, string Kind, string Name, string? Detail, string[] Options, string DefaultEnvironment);

    public static IEndpointRouteBuilder MapScriptIntegrationsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/v1/flows/{id:guid}/integrations", Get).RequireAuthorization();
        return app;
    }

    /// <summary>Node types and client-API endpoint ids across a script and the scripts it reaches.</summary>
    internal static (HashSet<string> NodeTypes, HashSet<Guid> EndpointIds) Walk(IReadOnlyDictionary<Guid, string> definitions, Guid start)
    {
        var types = new HashSet<string>();
        var endpoints = new HashSet<Guid>();
        var seen = new HashSet<Guid>();
        var queue = new Queue<Guid>([start]);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !definitions.TryGetValue(id, out var json)) continue;
            JsonObject? nodes;
            try { nodes = JsonNode.Parse(json)?["nodes"] as JsonObject; } catch (JsonException) { continue; }
            foreach (var (_, n) in nodes ?? [])
            {
                if (n is not JsonObject node || node["type"]?.GetValue<string>() is not { } type) continue;
                types.Add(type);
                string? S(string k) => node[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
                if (type == "api_call" && S("apiDefinitionScope") is not "portal" && Guid.TryParse(S("apiEndpointId"), out var ep)) endpoints.Add(ep);
                if (type is "execute_flow" or "transition_to_flow" && Guid.TryParse(S("targetFlowId"), out var next)) queue.Enqueue(next);
            }
        }
        return (types, endpoints);
    }

    private static async Task<IResult> Get(Guid id, ScopedTenantDbContextFactory dbf, TenantContext tc, CancellationToken ct)
    {
        if (!tc.HasTenant) return Results.Unauthorized();
        await using var db = dbf.Create();
        var flow = await db.Flows.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null) return Results.NotFound();

        var definitions = await db.Flows.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Definition, ct);
        var (types, endpointIds) = Walk(definitions, id);
        var items = new List<ScriptIntegration>();

        var taxProvider = flow.CampaignId is { } cid
            ? await db.Campaigns.AsNoTracking().Where(c => c.Id == cid).Select(c => c.TaxProvider).FirstOrDefaultAsync(ct)
            : null;
        if (!string.IsNullOrEmpty(taxProvider))
            items.Add(new(IntegrationEnvironment.Tax, "tax", $"Sales tax ({char.ToUpperInvariant(taxProvider[0])}{taxProvider[1..]})",
                "Sandbox uses the campaign's sandbox tax account, or simulated tax if it has none.",
                [IntegrationEnvironment.Sandbox, IntegrationEnvironment.Production], IntegrationEnvironment.Sandbox));

        if (types.Contains("authorize_payment") || types.Contains("void_payment"))
            items.Add(new(IntegrationEnvironment.Payment, "payment", "Payment gateway",
                "Sandbox uses the campaign's sandbox gateway account, or simulated approvals if it has none.",
                [IntegrationEnvironment.Sandbox, IntegrationEnvironment.Production], IntegrationEnvironment.Sandbox));

        if (endpointIds.Count > 0)
        {
            var endpoints = await db.TenantApiEndpoints.AsNoTracking().Where(e => endpointIds.Contains(e.Id))
                .Select(e => new { e.DefinitionId, e.Name }).ToListAsync(ct);
            var defIds = endpoints.Select(e => e.DefinitionId).Distinct().ToList();
            var defs = await db.TenantApiDefinitions.AsNoTracking().Where(d => defIds.Contains(d.Id)).OrderBy(d => d.Name).ToListAsync(ct);
            foreach (var d in defs)
            {
                var hasSandbox = !string.IsNullOrWhiteSpace(d.SandboxBaseUrl);
                string[] options = hasSandbox
                    ? [IntegrationEnvironment.Sandbox, IntegrationEnvironment.Production, IntegrationEnvironment.Simulated]
                    : [IntegrationEnvironment.Simulated, IntegrationEnvironment.Production];
                items.Add(new(IntegrationEnvironment.Api(d.Id), "api", d.Name,
                    string.Join(", ", endpoints.Where(e => e.DefinitionId == d.Id).Select(e => e.Name).Distinct()),
                    options, hasSandbox && d.DesignerSandboxUsesSandbox ? IntegrationEnvironment.Sandbox : IntegrationEnvironment.Simulated));
            }
        }
        return Results.Ok(items);
    }
}
