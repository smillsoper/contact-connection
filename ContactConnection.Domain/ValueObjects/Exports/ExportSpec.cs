namespace ContactConnection.Domain.ValueObjects.Exports;

/// <summary>
/// Everything that decides what an export file contains (S180, Export Worker): which calls, one row per what, and the
/// layout. Stored whole as JSONB on <see cref="Entities.ExportDefinition"/>, snapshotted into version history on every
/// save, and copied onto each <see cref="Entities.ExportRun"/> when it's queued — so a run always renders exactly the spec
/// it was asked for, and the test file a vendor approved can be reproduced byte for byte.
/// Immutable: edits replace the whole spec.
/// </summary>
public sealed record ExportSpec
{
    // ── Which calls ────────────────────────────────────────────────────────────
    public Guid? ClientId { get; init; }
    /// <summary>Empty = every campaign (of the client, when one is set). A call matches when its record or any of its
    /// interactions is on one of these campaigns.</summary>
    public List<Guid> CampaignIds { get; init; } = [];
    /// <summary>Media agency name the call is attributed to (case-insensitive); null = any.</summary>
    public string? MediaAgency { get; init; }
    /// <summary>One row per call, per interaction or per cart line — see <see cref="ExportRowGrain"/>.</summary>
    public string RowGrain { get; init; } = ExportRowGrain.Call;
    /// <summary>Optional Liquid condition (the inside of an <c>{% if %}</c>), e.g. <c>call.disposition != "Junk"</c>.
    /// Rows where it's false are left out.</summary>
    public string? Condition { get; init; }

    // ── Layout ─────────────────────────────────────────────────────────────────
    public string LayoutMode { get; init; } = ExportLayoutMode.Columns;
    /// <summary>Columns mode only: delimited / fixed / xlsx.</summary>
    public string Format { get; init; } = ExportFormat.Delimited;
    public string Delimiter { get; init; } = ",";
    public bool IncludeHeader { get; init; } = true;
    /// <summary><c>crlf</c> or <c>lf</c>.</summary>
    public string LineEnding { get; init; } = "crlf";
    public List<ExportColumn> Columns { get; init; } = [];
    /// <summary>Document mode: one Liquid template over <c>calls</c> (header lines, loops, trailer with counts).</summary>
    public string? DocumentTemplate { get; init; }
    /// <summary>Document mode: drop lines that are only whitespace (what Liquid tags leave behind).</summary>
    public bool SkipBlankLines { get; init; } = true;

    // ── File ───────────────────────────────────────────────────────────────────
    /// <summary>Liquid for the file name, e.g. <c>NERQ_TMS_{{ window.end | format_time: "MMddyy" }}.txt</c>.</summary>
    public string FileNameTemplate { get; init; } = "{{ export.name }}_{{ window.start | format_time: \"yyyyMMdd\" }}.csv";
    /// <summary>Inserted before the extension of a test file's name (blank = same name as production).</summary>
    public string TestFileSuffix { get; init; } = "_TEST";
    /// <summary>IANA time zone every date in the file is written in (<c>format_time</c>) and the window's dates are read in.</summary>
    public string TimeZone { get; init; } = "America/New_York";

    /// <summary>
    /// S182: the file carries card data (the <c>card</c> variables). Needs the tenant's Card data exports switch and the
    /// exports.card_data permission; delivery is FTPS + PGP only; the file is stored encrypted and can't be downloaded; the
    /// preview and test files are masked; each call's card data is wiped once every target confirms the file.
    /// </summary>
    public bool IncludesCardData { get; init; }
}

/// <param name="Header">Header line text (delimited / xlsx; fixed width pads it to the width).</param>
/// <param name="Template">Liquid for the value, e.g. <c>{{ call.ani | digits }}</c>.</param>
/// <param name="Width">Fixed width: the exact width (padded, then cut).</param>
/// <param name="Align">Fixed width: <c>left</c> (pad on the right) or <c>right</c>.</param>
/// <param name="PadChar">Fixed width: pad character (default space).</param>
/// <param name="Quote">Delimited: <c>auto</c> (only when needed), <c>always</c>, <c>never</c>.</param>
/// <param name="Type">xlsx: <c>text</c> or <c>number</c> (written as a number when it parses).</param>
public sealed record ExportColumn(
    string Header, string Template, int? Width = null, string Align = "left", string? PadChar = null,
    string Quote = "auto", string Type = "text");

public static class ExportRowGrain
{
    public const string Call = "call";
    public const string Interaction = "interaction";
    public const string CartLine = "cart_line";
    public static bool IsValid(string? v) => v is Call or Interaction or CartLine;
}

public static class ExportLayoutMode
{
    public const string Columns = "columns";
    public const string Document = "document";
    public static bool IsValid(string? v) => v is Columns or Document;
}

public static class ExportFormat
{
    public const string Delimited = "delimited";
    public const string Fixed = "fixed";
    public const string Xlsx = "xlsx";
    public static bool IsValid(string? v) => v is Delimited or Fixed or Xlsx;
}
