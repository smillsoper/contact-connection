using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using ContactConnection.Domain.Entities;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Infrastructure.FlowDocs;

/// <summary>
/// Builds the flow document for one flow: that flow as chapter 1, then every script it runs, hands off to or pops for
/// the agent (execute_flow / transition_to_flow / tf_script_pop) as further chapters. A published document shows
/// published scripts — a called script that isn't published gets a warning instead (calls reaching it fail); a draft
/// document shows every script's saved draft.
/// </summary>
public class FlowDocumentService(IHttpClientFactory httpClients)
{
    private const int MaxChapters = 15;

    public async Task<(byte[] Pdf, string FileName, FlowDocument Doc)> BuildAsync(TenantDbContext db, Flow root, bool draft,
        Tenant tenant, string generatedBy, CancellationToken ct)
    {
        var flowNames = await db.Flows.AsNoTracking().ToDictionaryAsync(f => f.Id, f => f.Name, ct);
        var lookups = new FlowDocLookups(
            flowNames,
            await db.AudioFiles.AsNoTracking().ToDictionaryAsync(a => a.Id, a => a.Name, ct),
            await db.Campaigns.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct),
            await db.AgentGroups.AsNoTracking().ToDictionaryAsync(g => g.Id, g => g.Name, ct));

        var (logo, isSvg) = await FetchLogoAsync(tenant.LogoUrl, ct);
        var doc = new FlowDocument
        {
            TenantName = tenant.DisplayName ?? tenant.Name, Logo = logo, LogoIsSvg = isSvg,
            Title = root.Name, Draft = draft, GeneratedBy = generatedBy, GeneratedAt = DateTimeOffset.UtcNow, TimeZone = tenant.Timezone,
        };

        var queue = new Queue<Flow>([root]);
        var seen = new HashSet<Guid> { root.Id };
        while (queue.Count > 0 && doc.Chapters.Count < MaxChapters)
        {
            var flow = queue.Dequeue();
            var telephony = flow.FlowType == FlowType.Telephony;
            DocChapter chapter;
            if (!draft && (!flow.IsActive || flow.PublishedDefinition is null))
            {
                chapter = new DocChapter
                {
                    FlowId = flow.Id, FlowName = flow.Name, IsTelephony = telephony, Version = flow.Version, IsDraft = true,
                    Warning = "Not published — calls that reach this script fail until it is. Generate a draft document to see it.",
                };
            }
            else
            {
                var json = JsonNode.Parse(flow.DefinitionFor(draft)) as JsonObject ?? [];
                chapter = FlowDocExtractor.Build(json, flow.Id, flow.Name, telephony, flow.VersionFor(draft), draft, lookups);
            }
            // A script pop that doesn't name a script opens the flow's campaign's script — document that one.
            if (flow.CampaignId is { } campaignId
                && chapter.StepsById.Values.Where(s => s.Type == "tf_script_pop").ToList() is { Count: > 0 } pops
                && await db.Campaigns.AsNoTracking().Where(c => c.Id == campaignId).Select(c => c.FlowId).FirstOrDefaultAsync(ct) is { } campaignFlow)
                foreach (var pop in pops.Where(p => p.CallsFlowId is null))
                {
                    pop.CallsFlowId = campaignFlow;
                    pop.Facts.Add(new DocFact("Opens the agent script", $"{flowNames.GetValueOrDefault(campaignFlow, "the campaign's script")} (the campaign's script)"));
                }
            chapter.Number = doc.Chapters.Count + 1;
            doc.Chapters.Add(chapter);

            var called = chapter.StepsById.Values.Select(s => s.CallsFlowId).OfType<Guid>().Where(seen.Add).ToList();
            if (called.Count == 0) continue;
            var flows = await db.Flows.AsNoTracking().Where(f => called.Contains(f.Id)).ToListAsync(ct);
            foreach (var id in called)
                if (flows.FirstOrDefault(f => f.Id == id) is { } f) queue.Enqueue(f);
        }

        var pdf = FlowDocumentPdf.Render(doc);
        var version = draft ? $"draft v{root.Version}" : $"v{root.PublishedVersion}";
        var safe = new string(root.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray()).Trim();
        return (pdf, $"{safe} - {version}.pdf", doc);
    }

    /// <summary>
    /// The tenant's logo for the cover. The address is tenant-entered, so the fetch is fenced: https only, public
    /// addresses only (no internal network / metadata endpoints), no redirects, 5 s, 2 MB, images only. Any failure just
    /// leaves the logo off.
    /// </summary>
    private async Task<(byte[]? Bytes, bool IsSvg)> FetchLogoAsync(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return (null, false);
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, ct);
            if (addresses.Length == 0 || addresses.Any(IsPrivate)) return (null, false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            var client = httpClients.CreateClient("flow-doc-logo");
            using var res = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cts.Token);
            if (!res.IsSuccessStatusCode) return (null, false);
            var type = res.Content.Headers.ContentType?.MediaType ?? "";
            var isSvg = type.Contains("svg", StringComparison.OrdinalIgnoreCase) || uri.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase);
            if (!isSvg && type is not ("image/png" or "image/jpeg" or "image/webp")) return (null, false);
            if (res.Content.Headers.ContentLength is > 2_000_000) return (null, false);
            await using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[16384];
            int read;
            while ((read = await stream.ReadAsync(chunk, cts.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > 2_000_000) return (null, false);
            }
            return (buffer.ToArray(), isSvg);
        }
        catch (Exception) { return (null, false); }
    }

    /// <summary>The logo client's handler: no redirects, and the connection itself refuses non-public addresses (so a
    /// name that resolves differently on the second lookup can't reach the internal network).</summary>
    public static SocketsHttpHandler CreateLogoHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var target = addresses.FirstOrDefault(a => !IsPrivate(a))
                ?? throw new HttpRequestException("The logo address isn't a public host.");
            if (addresses.Any(IsPrivate)) throw new HttpRequestException("The logo address isn't a public host.");
            var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try { await socket.ConnectAsync(target, context.DnsEndPoint.Port, ct); return new NetworkStream(socket, ownsSocket: true); }
            catch { socket.Dispose(); throw; }
        },
    };

    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || ip.IsIPv6UniqueLocal || ip.Equals(IPAddress.IPv6Any);
        var b = ip.GetAddressBytes();
        return b[0] switch
        {
            0 or 10 or 127 => true,
            100 => b[1] >= 64 && b[1] < 128,           // carrier-grade NAT
            169 => b[1] == 254,                          // link-local / cloud metadata
            172 => b[1] >= 16 && b[1] < 32,
            192 => b[1] == 168 || (b[1] == 0 && b[2] == 0),
            >= 224 => true,                              // multicast / reserved
            _ => false,
        };
    }
}
