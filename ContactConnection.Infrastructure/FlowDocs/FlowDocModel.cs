namespace ContactConnection.Infrastructure.FlowDocs;

// Flow document (S183): a client-ready PDF of a CRM script or call flow — an automatically laid-out chart plus the full
// script, step by step. Built from the flow definition, so it's complete and current. Never prints web addresses,
// credentials, card data or internal variable names.

/// <summary>How a step is drawn (colour) and how much room it gets.</summary>
public enum StepKind
{
    Section,      // a named section of the script
    Speak,        // the agent reads / captures something (script, input, email, phone, address)
    Decision,     // branch, IVR menu, time of day
    Commerce,     // cart, payment, order
    Integration,  // API call, email, sub-script
    Audio,        // telephony: plays / collects audio
    Routing,      // telephony: queue, transfer, callback, script pop
    Event,        // telephony: an event handler ("when the agent answers")
    System,       // behind-the-scenes bookkeeping (set variable, custom field…) — drawn small
    End,
}

/// <summary>A run of script text with its formatting.</summary>
public record DocRun(string Text, bool Bold = false, bool Italic = false, bool Underline = false, string? Color = null);

/// <summary>A paragraph (or list item) of script text.</summary>
public record DocParagraph(IReadOnlyList<DocRun> Runs, bool Bullet = false);

/// <summary>A labelled fact about a step ("Captures", "Checks", "Options"…).</summary>
public record DocFact(string Label, string Value);

/// <summary>An exit: its readable label (null = the only/default way on), where it goes, and the designer's handle.</summary>
public record DocExit(string? Label, string TargetId, string Handle = "default");

public class DocStep
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public required StepKind Kind { get; init; }
    /// <summary>Short name shown in the chart box.</summary>
    public required string Title { get; init; }
    /// <summary>Plain-English kind ("Agent input", "Plays audio").</summary>
    public required string TypeLabel { get; init; }
    public int Number { get; set; }
    public string GroupKey { get; set; } = "";
    /// <summary>What the agent reads / the caller hears.</summary>
    public List<DocParagraph> Script { get; } = [];
    public List<DocFact> Facts { get; } = [];
    public List<DocExit> Exits { get; } = [];
    /// <summary>Set for a step that runs another script (its chapter number, filled in later).</summary>
    public Guid? CallsFlowId { get; set; }
    public bool Unreachable { get; set; }
}

/// <summary>A run of steps drawn on one chart: a section of a CRM script, or a call flow's main path / one event
/// handler.</summary>
public class DocGroup
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public string? Subtitle { get; init; }
    public List<DocStep> Steps { get; } = [];
}

public class DocChapter
{
    public int Number { get; set; }
    public required Guid FlowId { get; init; }
    public required string FlowName { get; init; }
    public required bool IsTelephony { get; init; }
    public required int Version { get; init; }
    public required bool IsDraft { get; init; }
    /// <summary>A called script that isn't published while the document shows published versions — calls reaching it
    /// fail; the chapter only carries this warning.</summary>
    public string? Warning { get; init; }
    public List<DocGroup> Groups { get; } = [];
    public Dictionary<string, DocStep> StepsById { get; } = [];
}

public class FlowDocument
{
    public required string TenantName { get; init; }
    public byte[]? Logo { get; init; }
    public bool LogoIsSvg { get; init; }
    public required string Title { get; init; }
    public required bool Draft { get; init; }
    public required string GeneratedBy { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required string TimeZone { get; init; }
    public List<DocChapter> Chapters { get; } = [];
}
