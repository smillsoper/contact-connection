namespace ContactConnection.Domain.Entities;

/// <summary>
/// An AI call summary through its life (AI learning track step "b", S171): every generation is saved as a
/// <see cref="CallSummaryStatus.Suggested"/> row — what the model said, which model, tokens and cost — and a
/// person then confirms it (as-is or edited) or discards it. Human-in-the-loop with provenance:
/// <list type="bullet">
/// <item>the AI's original is stored on the server when generated, so the browser can never misrepresent it;</item>
/// <item>the confirmed version records who confirmed it, when, and whether they edited it — giving
/// acceptance and edit rates, the quality measures for an AI-assist feature;</item>
/// <item>a newer confirmed summary supersedes the older one; nothing is deleted.</item>
/// </list>
/// </summary>
public class CallSummary
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CallRecordId { get; private set; }
    public string Status { get; private set; } = CallSummaryStatus.Suggested;

    // ── What the AI suggested ──
    public string AiSummary { get; private set; } = "";
    public string AiReasonForCall { get; private set; } = "";
    public string AiOutcome { get; private set; } = "";
    public string? AiDisposition { get; private set; }
    public bool AiDispositionValid { get; private set; }
    public double AiConfidence { get; private set; }
    public string? AiFollowUp { get; private set; }
    public bool AiIsTestCall { get; private set; }
    /// <summary>Detected by our code (placeholder answers), independent of the model.</summary>
    public bool PossibleTestCall { get; private set; }
    public string Model { get; private set; } = "";
    public int InputTokens { get; private set; }
    public int OutputTokens { get; private set; }
    public decimal CostUsd { get; private set; }
    public long ElapsedMs { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public string? CreatedByName { get; private set; }

    // ── What a person confirmed ──
    public string? Summary { get; private set; }
    public string? ReasonForCall { get; private set; }
    public string? Outcome { get; private set; }
    public string? Disposition { get; private set; }
    public string? FollowUp { get; private set; }
    /// <summary>True when the person changed anything the AI suggested.</summary>
    public bool Edited { get; private set; }
    public Guid? ReviewedById { get; private set; }
    public string? ReviewedByName { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }

    private CallSummary() { }

    public static CallSummary Suggest(
        Guid tenantId, Guid callRecordId,
        string summary, string reasonForCall, string outcome, string? disposition, bool dispositionValid,
        double confidence, string? followUp, bool aiIsTestCall, bool possibleTestCall,
        string model, int inputTokens, int outputTokens, decimal costUsd, long elapsedMs, string? createdByName) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, CallRecordId = callRecordId, Status = CallSummaryStatus.Suggested,
        AiSummary = summary, AiReasonForCall = reasonForCall, AiOutcome = outcome,
        AiDisposition = dispositionValid ? disposition : null, AiDispositionValid = dispositionValid,
        AiConfidence = confidence, AiFollowUp = followUp, AiIsTestCall = aiIsTestCall, PossibleTestCall = possibleTestCall,
        Model = model, InputTokens = inputTokens, OutputTokens = outputTokens, CostUsd = costUsd, ElapsedMs = elapsedMs,
        CreatedAt = DateTimeOffset.UtcNow, CreatedByName = createdByName,
    };

    /// <summary>A person accepts the suggestion — as-is or edited. Only a pending suggestion can be confirmed.</summary>
    public void Confirm(
        string summary, string reasonForCall, string outcome, string? disposition, string? followUp,
        Guid reviewerId, string reviewerName)
    {
        if (Status != CallSummaryStatus.Suggested) throw new InvalidOperationException("This summary has already been reviewed.");
        if (string.IsNullOrWhiteSpace(summary)) throw new ArgumentException("The summary can't be empty.");

        Summary = summary.Trim();
        ReasonForCall = Blank(reasonForCall) ?? "";
        Outcome = outcome;
        Disposition = Blank(disposition);
        FollowUp = Blank(followUp);
        Edited = Summary != AiSummary.Trim() || ReasonForCall != AiReasonForCall.Trim() || Outcome != AiOutcome
                 || !string.Equals(Disposition, AiDisposition, StringComparison.Ordinal)
                 || !string.Equals(FollowUp, Blank(AiFollowUp), StringComparison.Ordinal);
        Status = CallSummaryStatus.Confirmed;
        ReviewedById = reviewerId;
        ReviewedByName = reviewerName;
        ReviewedAt = DateTimeOffset.UtcNow;
    }

    public void Discard(Guid reviewerId, string reviewerName)
    {
        if (Status != CallSummaryStatus.Suggested) throw new InvalidOperationException("This summary has already been reviewed.");
        Status = CallSummaryStatus.Discarded;
        ReviewedById = reviewerId;
        ReviewedByName = reviewerName;
        ReviewedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>A newer confirmed summary replaced this one.</summary>
    public void Supersede()
    {
        if (Status == CallSummaryStatus.Confirmed) Status = CallSummaryStatus.Superseded;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class CallSummaryStatus
{
    public const string Suggested  = "suggested";
    public const string Confirmed  = "confirmed";
    public const string Discarded  = "discarded";
    public const string Superseded = "superseded";
}
