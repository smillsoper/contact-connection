using ContactConnection.Application.Interfaces.Repositories;
using ContactConnection.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace ContactConnection.Api.Endpoints;

/// <summary>
/// Handles mod_xml_curl directory lookups from FreeSWITCH.
/// FreeSWITCH POSTs form-encoded data; we return FreeSWITCH XML with the agent's a1-hash.
/// No bearer auth — this endpoint is internal-network only (FreeSWITCH container → API host).
/// </summary>
public static class FreeSwitchDirectoryEndpoints
{
    public static IEndpointRouteBuilder MapFreeSwitchDirectoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/v1/freeswitch/directory", Directory).AllowAnonymous().DisableAntiforgery();
        return app;
    }

    private static async Task<IResult> Directory(
        IFormCollection form,
        ITenantRepository tenants,
        ITenantDbContextFactory dbFactory,
        CancellationToken ct)
    {
        var section = form["section"].ToString();
        var user    = form["user"].ToString();
        var domain  = form["domain"].ToString();

        if (section != "directory" || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(domain))
            return Xml(NotFoundXml());

        var subdomain = ExtractSubdomain(domain);
        if (subdomain is null)
            return Xml(NotFoundXml());

        var tenant = await tenants.GetBySubdomainAsync(subdomain, ct);
        if (tenant is null)
            return Xml(NotFoundXml());

        await using var db = dbFactory.Create(tenant.SchemaName);
        var agent = await db.Agents
            .FirstOrDefaultAsync(a => a.SipExtension == user && a.IsActive, ct);

        if (agent?.SipA1Hash is null)
            return Xml(NotFoundXml());

        return Xml(DirectoryXml(domain, agent.SipExtension!, agent.SipA1Hash, agent.FullName));
    }

    // Extracts the leading subdomain segment from a SIP domain string.
    // "tms.hubion.local" → "tms", "127.0.0.1" → null (IPs not resolvable to a tenant).
    private static string? ExtractSubdomain(string domain)
    {
        if (System.Net.IPAddress.TryParse(domain, out _)) return null;
        var dot = domain.IndexOf('.');
        return dot > 0 ? domain[..dot] : domain;
    }

    private static IResult Xml(string xml) =>
        Results.Content(xml, "text/xml; charset=utf-8");

    // FreeSWITCH expands ${sofia_contact(...)} at runtime using its registration table —
    // this prevents FreeSWITCH from doing a raw DNS lookup on the SIP domain name.
    private const string DialString =
        "{presence_id=${dialed_user}@${dialed_domain}}${sofia_contact(${dialed_user}@${dialed_domain})}";

    private static string DirectoryXml(string domain, string extension, string a1hash, string agentName) => $"""
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <document type="freeswitch/xml">
          <section name="directory">
            <domain name="{domain}">
              <user id="{extension}">
                <params>
                  <param name="a1-hash"     value="{a1hash}"/>
                  <param name="dial-string" value="{DialString}"/>
                </params>
                <variables>
                  <variable name="user_context"               value="default"/>
                  <variable name="effective_caller_id_name"   value="{agentName}"/>
                  <variable name="effective_caller_id_number" value="{extension}"/>
                </variables>
              </user>
            </domain>
          </section>
        </document>
        """;

    private static string NotFoundXml() => """
        <?xml version="1.0" encoding="UTF-8" standalone="no"?>
        <document type="freeswitch/xml">
          <section name="directory">
            <result status="not found"/>
          </section>
        </document>
        """;
}
