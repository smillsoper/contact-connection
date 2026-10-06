using ContactConnection.Domain.ValueObjects.Exports;

namespace ContactConnection.Infrastructure.Exports;

/// <summary>
/// Ready-made exports the editor offers under "Start from" (S180, session 2). The Cannella pair reproduces the CRMPro
/// layouts Life Seasons sends today (docs/export-engine-plan.md) — layout only; codes that differ per client come from the
/// call's media assignment fields (client_code, access_code, product_code), with Life Seasons' values as fallbacks.
/// Every date is true Eastern time (CRMPro's LF shifted Pacific +6 h — a bug, deliberately not reproduced).
/// Anything marked "check with the vendor" is exactly what a test file is for.
/// </summary>
public static class ExportStarterTemplates
{
    public sealed record StarterTemplate(string Key, string Name, string Description, string Notes, ExportSpec Spec, ExportSchedule Schedule);

    private static readonly ExportSchedule Nightly = new()
    {
        Frequency = ExportFrequency.Daily, TimeOfDay = "21:30", TimeZone = "America/Los_Angeles",
        Window = ExportWindowKind.PreviousDay, AutoDeliver = true,
    };

    /// <summary>Shared per-call values (Liquid assigns) for both Cannella files.</summary>
    private const string CallValues = """
        {%- assign junk = false -%}{%- if call.disposition contains "Junk" -%}{%- assign junk = true -%}{%- endif -%}
        {%- assign tfn = call.dnis | digits | slice: -10, 10 -%}
        {%- assign station = call.media.station -%}
        {%- assign zip = call.billing_address.zip | slice: 0, 5 -%}{%- if zip == "NOZIP" -%}{%- assign zip = "" -%}{%- endif -%}
        {%- assign ani = call.ani | digits | slice: -10, 10 -%}
        """;

    public static readonly StarterTemplate CannellaLf = new(
        "cannella_lf", "Cannella LF (nightly CSV)",
        "Cannella long-form file: CALL / ORDER / UPSELL / REVENUE rows per call, CSV, previous Eastern day.",
        "Check with Cannella: the client code (media field client_code, else 13156), the call-type wording, and that UPSELL rows " +
        "come from the offer flags \"Cannella Order SKU\" / \"Cannella Upsell SKU\". Junk calls are left out.",
        new ExportSpec
        {
            MediaAgency = "Cannella",
            RowGrain = ExportRowGrain.Call,
            Condition = "call.media.ad_type != \"SF\"",
            LayoutMode = ExportLayoutMode.Document,
            LineEnding = "crlf",
            SkipBlankLines = true,
            TimeZone = "America/New_York",
            TestFileSuffix = "_TEST",
            FileNameTemplate = "DAT_TEMS_LIFESEASONS_0_{{ export.generated_at | format_time: 'yyyyMMddHHmmss' }}.csv",
            DocumentTemplate = """
                {%- for call in calls -%}
                """ + CallValues + """

                {%- unless junk -%}
                {%- assign cc = call.media.fields.client_code | default: "13156" -%}
                {%- capture head -%}"TEMS","TEMS",{{ cc | csv }},{{ call.order_number | csv }},{{ call.started_at | format_time: 'MM/dd/yyyy' | csv }},{{ call.started_at | format_time: 'HH:mm:ss' | csv }}{%- endcapture -%}
                {%- capture addr -%}{{ ani }},{{ call.billing_address.address1 | csv }},{{ call.billing_address.address2 | csv }},{{ call.billing_address.city | csv }},{{ call.billing_address.state | csv }},{% if call.billing_address.zip4 != "" %}{{ call.billing_address.zip | append: "-" | append: call.billing_address.zip4 | csv }}{% else %}{{ call.billing_address.zip | csv }}{% endif %}{%- endcapture -%}
                {%- capture media -%}{{ tfn | default: "UNASSIGNED" | csv }},{{ station | default: "UNASSIGNED" | csv }},{{ call.media.fields.product_code | csv }},{{ call.area_code }},"",{{ zip | csv }}{%- endcapture -%}
                {%- if call.has_order -%}{%- assign calltype = "Order" -%}{%- elsif call.disposition contains "Customer Service" -%}{%- assign calltype = "Customer Service" -%}{%- else -%}{%- assign calltype = "Inquiry" -%}{%- endif -%}
                {{ head }},"CALL","",1,"Valid",{{ media }},{{ calltype | csv }},{{ addr }}
                {% if call.has_order %}{{ head }},"ORDER","",1,"",{{ media }},"",{{ addr }}
                {% for ix in call.interactions %}{% if ix.has_order %}{% for line in ix.cart.items %}{% assign code = line.flags["Cannella Upsell SKU"] | default: line.flags["Cannella Order SKU"] %}{% if code != blank %}{{ head }},"UPSELL",{{ code | csv }},{{ line.quantity }},"",{{ media }},"",{{ addr }}
                {% endif %}{% endfor %}{% endif %}{% endfor %}{{ head }},"REVENUE","",{{ call.order_total | minus: call.order_tax | money }},"",{{ media }},"",{{ addr }}
                {% endif %}
                {%- endunless -%}
                {%- endfor -%}
                """,
        },
        Nightly);

    public static readonly StarterTemplate CannellaSf = new(
        "cannella_sf", "Cannella SF (nightly CORE fixed width)",
        "Cannella short-form CORE file: VCAL / CALL / ORD / GREV records per call, 108-character fixed width, previous Eastern day.",
        "Check with Cannella: GREV is the order total in whole dollars (000000); the date in the file name is the day the file " +
        "covers; a re-run of a past day is flagged R instead of O.",
        new ExportSpec
        {
            MediaAgency = "Cannella",
            RowGrain = ExportRowGrain.Call,
            Condition = "call.media.ad_type == \"SF\"",
            LayoutMode = ExportLayoutMode.Document,
            LineEnding = "crlf",
            SkipBlankLines = true,
            TimeZone = "America/New_York",
            TestFileSuffix = "_TEST",
            FileNameTemplate = "NERQ_TMS_{{ window.end_inclusive | format_time: 'MMddyy' }}.txt",
            DocumentTemplate = """
                {%- assign flag = "O" -%}{%- if export.is_rerun -%}{%- assign flag = "R" -%}{%- endif -%}
                {%- for call in calls -%}
                """ + CallValues + """

                {%- assign area = call.billing_phone | digits | slice: -10, 3 -%}{%- if area == "" -%}{%- assign area = call.area_code -%}{%- endif -%}
                {%- capture base -%}TMSS{{ flag }}{{ "" | pad_right: 12 }}TVLIFE{{ "" | pad_right: 8 }}{{ call.media.fields.access_code | pad_right: 4 }}{{ station | pad_right: 12 }}{{ call.started_at | format_time: 'yyyyMMdd' }}{{ call.started_at | format_time: 'HHmm' }}{%- endcapture -%}
                {%- capture tail -%}{{ tfn | pad_right: 10 }}{{ zip | pad_right: 5 }}{{ area | pad_right: 3 }}{{ "" | pad_right: 21 }}{%- endcapture -%}
                {{ base }}VCAL000001{{ tail }}
                {% unless junk %}{{ base }}CALL000001{{ tail }}
                {% endunless %}{% if call.has_order and call.source == "inbound" %}{{ base }}ORD 000001{{ tail }}
                {{ base }}GREV{{ call.order_total | round | number: '000000' }}{{ tail }}
                {% endif %}
                {%- endfor -%}
                """,
        },
        Nightly);

    public static readonly IReadOnlyList<StarterTemplate> All = [CannellaLf, CannellaSf];
}
