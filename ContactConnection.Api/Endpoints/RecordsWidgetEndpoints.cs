using ContactConnection.Application.Services;
using ContactConnection.Infrastructure.Reports;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Records widget, internal dashboards (S181, docs/client-dashboards-plan.md §C): paginated production call records with
/// chosen columns, search and column filters, and a detail view. Recordings play through the existing
/// <c>/call-records/{id}/recording</c>. The client portal serves the same data through its own scoped endpoints.
/// </summary>
public static class RecordsWidgetEndpoints
{
    public static IEndpointRouteBuilder MapRecordsWidgetEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/v1/dashboard-widgets/records").RequireAuthorization("ReportsView");
        g.MapGet("", Query);
        g.MapGet("columns", () => Results.Ok(RecordColumns.All));
        g.MapGet("{id:guid}", Detail);
        return app;
    }

    /// <summary>Page / search / sort / column filters ("f.&lt;column&gt;=text") from a request's query string.</summary>
    internal static (int Page, int PageSize, string? Search, string? Sort, bool Desc, Dictionary<string, string> Filters) Paging(HttpRequest r, int defaultPageSize)
    {
        var q = r.Query;
        int Int(string k, int d) => int.TryParse(q[k], out var v) ? v : d;
        var filters = q.Where(kv => kv.Key.StartsWith("f.") && !string.IsNullOrWhiteSpace(kv.Value))
            .ToDictionary(kv => kv.Key[2..], kv => kv.Value.ToString());
        return (Int("page", 1), Int("pageSize", defaultPageSize), q["search"].ToString() is { Length: > 0 } s ? s : null,
            q["sort"].ToString() is { Length: > 0 } so ? so : null, q["desc"] != "false", filters);
    }

    internal static List<string> Columns(string? csv) =>
        (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(RecordColumns.IsValid).ToList();

    private static async Task<IResult> Query(Guid? clientId, Guid? campaignId, string? timeWindowMode, int? timeWindowValue, string? columns,
        HttpRequest request, CallRecordsReport report, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        var (since, until) = KpiEndpoints.Window(tenant.Timezone, timeWindowMode, timeWindowValue, DateTimeOffset.UtcNow);
        var p = Paging(request, 25);
        return Results.Ok(await report.QueryAsync(new RecordsQuery(since, until, campaignId is null ? clientId : null, campaignId, null,
            Columns(columns), p.Search, p.Filters, p.Sort, p.Desc, p.Page, p.PageSize, tenant.Timezone), ct));
    }

    private static async Task<IResult> Detail(Guid id, Guid? clientId, Guid? campaignId, bool? allowRecordings, CallRecordsReport report,
        [AsParameters] CallDetailView.Deps deps, TenantContext tc, CancellationToken ct)
    {
        if (tc.Current is not { } tenant) return Results.Unauthorized();
        if (!await report.InScopeAsync(id, campaignId is null ? clientId : null, campaignId is { } c ? new HashSet<Guid> { c } : null, ct))
            return Results.NotFound();
        var built = await CallDetailView.BuildAsync(id, deps, tenant.Timezone, ct);
        if (built is not { } b) return Results.NotFound();
        return Results.Ok(new { detail = b.Detail, canPlayRecording = b.RecordingStatus == "available" && allowRecordings != false });
    }
}
