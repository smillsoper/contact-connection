using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ContactConnection.Domain.Entities;

// Number porting (S184). SignalWire has no porting API, so the platform automates everything around the manual step:
// the tenant pastes numbers, we scrub and split them into orders (SignalWire takes one service address per order, and
// toll-free goes through a different LOA), the old carrier account's owner fills in and e-signs SignalWire's own LOA
// through a secure link, and the porting mailbox gets everything needed to enter it in SignalWire's portal. The Portal's
// Porting queue tracks it to completion.

public static class PortNumbers
{
    /// <summary>US / Canada toll-free area codes.</summary>
    public static readonly IReadOnlySet<string> TollFreeAreaCodes = new HashSet<string> { "800", "833", "844", "855", "866", "877", "888" };

    public sealed record Parsed(List<string> Numbers, List<string> Invalid);

    /// <summary>Splits pasted text (lines, commas, semicolons, tabs) into E.164 NANP numbers (+1XXXXXXXXXX), deduped, in
    /// paste order. Anything that isn't a 10-digit NANP number (optionally with a leading 1) is listed as invalid.</summary>
    public static Parsed Parse(string? text)
    {
        var numbers = new List<string>();
        var invalid = new List<string>();
        foreach (var raw in Regex.Split(text ?? "", @"[\r\n,;\t]+"))
        {
            var piece = raw.Trim();
            if (piece.Length == 0) continue;
            var digits = new string(piece.Where(char.IsDigit).ToArray());
            if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
            // NANP: area code and exchange can't start with 0 or 1.
            if (digits.Length != 10 || digits[0] is '0' or '1' || digits[3] is '0' or '1' || Regex.IsMatch(piece, "[A-Za-z]"))
            {
                if (!invalid.Contains(piece)) invalid.Add(piece);
                continue;
            }
            var e164 = "+1" + digits;
            if (!numbers.Contains(e164)) numbers.Add(e164);
        }
        return new Parsed(numbers, invalid);
    }

    public static bool IsTollFree(string e164) => e164.Length == 12 && TollFreeAreaCodes.Contains(e164.Substring(2, 3));

    /// <summary>(503) 555-1234 for people; the form and LOA use it too.</summary>
    public static string Display(string e164) =>
        e164.Length == 12 && e164.StartsWith("+1") ? $"({e164[2..5]}) {e164[5..8]}-{e164[8..]}" : e164;
}

public static class PortOrderKind
{
    public const string Local = "local";
    public const string TollFree = "toll_free";
}

public static class PortOrderStatus
{
    /// <summary>Waiting for the carrier account's owner to fill in and sign.</summary>
    public const string AwaitingSignature = "awaiting_signature";
    /// <summary>Signed — the porting person enters it in SignalWire.</summary>
    public const string ReadyToSubmit = "ready_to_submit";
    /// <summary>Entered in SignalWire (their order number recorded).</summary>
    public const string Submitted = "submitted";
    /// <summary>Something was wrong — the signer has a new link and our message.</summary>
    public const string NeedsCorrection = "needs_correction";
    /// <summary>The carrier confirmed the port date (FOC) — numbers are loaded ahead so they work when it flips.</summary>
    public const string FocConfirmed = "foc_confirmed";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [AwaitingSignature, ReadyToSubmit, Submitted, NeedsCorrection, FocConfirmed, Completed, Cancelled];
    public static bool IsOpen(string s) => s is not (Completed or Cancelled);
}

/// <summary>SignalWire's "Services to Port". Voice only until the platform has messaging (then add the other two and the
/// 10DLC Campaign ID field).</summary>
public static class PortServices
{
    public const string Voice = "Voice Services Only";
}

/// <summary>One line on an order's timeline (who did what, when) — shown to the tenant and in the Portal.</summary>
public sealed record PortEvent(DateTimeOffset At, string By, string Text);

/// <summary>What the carrier account's owner fills in on the signing page.</summary>
public sealed record PortSignerDetails
{
    public string AuthorizedName { get; init; } = "";
    public string? AuthorizedTitle { get; init; }
    public string BillingName { get; init; } = "";
    public string? AccountNumber { get; init; }
    public string BillingPhone { get; init; } = "";
    public string? CurrentProvider { get; init; }
    public string? LongDistanceProvider { get; init; }
    public string ServiceStreet { get; init; } = "";
    public string? ServiceUnit { get; init; }
    public string ServiceCity { get; init; } = "";
    public string ServiceState { get; init; } = "";
    public string ServiceZip { get; init; } = "";
    public string? MailingAddress { get; init; }
    public string? AlternateContact { get; init; }

    public string ServiceAddressLine =>
        $"{ServiceStreet}{(string.IsNullOrWhiteSpace(ServiceUnit) ? "" : " " + ServiceUnit)}, {ServiceCity}, {ServiceState} {ServiceZip}".Trim();

    /// <summary>The problems with these answers, in plain words (empty = fine).</summary>
    public List<string> Problems(string kind)
    {
        var p = new List<string>();
        var parts = AuthorizedName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) p.Add("Enter the account owner's legal first and last name.");
        if (kind == PortOrderKind.TollFree && string.IsNullOrWhiteSpace(AuthorizedTitle)) p.Add("Enter the signer's title.");
        if (string.IsNullOrWhiteSpace(BillingName)) p.Add("Enter the billing name exactly as it appears on the bill.");
        if (PortNumbers.Parse(BillingPhone).Numbers.Count != 1) p.Add("Enter the account's billing phone number.");
        if (kind == PortOrderKind.Local && string.IsNullOrWhiteSpace(CurrentProvider)) p.Add("Enter the current phone company.");
        if (string.IsNullOrWhiteSpace(ServiceStreet) || string.IsNullOrWhiteSpace(ServiceCity) || string.IsNullOrWhiteSpace(ServiceState) || string.IsNullOrWhiteSpace(ServiceZip))
            p.Add("Enter the full service address.");
        else if (Regex.IsMatch(ServiceStreet, @"\b(p\.?\s*o\.?\s*box|post\s+office\s+box)\b", RegexOptions.IgnoreCase))
            p.Add("The service address must be where the phones are — not a PO box.");
        return p;
    }
}

public class PortOrder
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromDays(14);
    /// <summary>SignalWire wants the LOA dated within 30 days of submission.</summary>
    public static readonly TimeSpan SignatureValidFor = TimeSpan.FromDays(30);
    /// <summary>The PIN and bill copy are deleted this long after the port completes (or is cancelled).</summary>
    public static readonly TimeSpan SensitiveRetention = TimeSpan.FromDays(30);

    public Guid Id { get; private set; }
    /// <summary>Running number → the reference "P-1001" (the numbers' label once they're loaded).</summary>
    public int Number { get; private set; }
    public Guid TenantId { get; private set; }
    public string Kind { get; private set; } = PortOrderKind.Local;
    public List<string> Numbers { get; private set; } = [];
    public string Services { get; private set; } = PortServices.Voice;
    public string Status { get; private set; } = PortOrderStatus.AwaitingSignature;

    // The tenant's part
    public string AccountType { get; private set; } = "Business";
    public string EndUserName { get; private set; } = "";
    public string? CurrentProviderHint { get; private set; }
    public Guid? PreAssignCampaignId { get; private set; }
    public Guid RequestedById { get; private set; }
    public string RequestedByName { get; private set; } = "";
    public string RequestedByEmail { get; private set; } = "";
    public string SignerEmail { get; private set; } = "";

    // The signer's part
    public PortSignerDetails? Signer { get; private set; }
    /// <summary>The account PIN, encrypted (ISensitiveDataProtector) — null once purged.</summary>
    public string? PinProtected { get; private set; }
    public bool PinNotApplicable { get; private set; }
    public string? BillFileKey { get; private set; }
    public string? BillFileName { get; private set; }
    public string? BillContentType { get; private set; }
    public string? SignatureName { get; private set; }
    public DateTimeOffset? SignedAt { get; private set; }
    public string? SignerIp { get; private set; }
    public string? SignerUserAgent { get; private set; }
    public string? LoaFileKey { get; private set; }
    public string? LoaSha256 { get; private set; }
    public string? CertificateFileKey { get; private set; }

    // Signing link (only its hash is stored)
    public string? TokenHash { get; private set; }
    public DateTimeOffset? TokenExpiresAt { get; private set; }
    public string? CorrectionMessage { get; private set; }

    // The porting desk
    public string? SignalWireOrderNumber { get; private set; }
    public DateOnly? FocDate { get; private set; }
    public DateTimeOffset? NumbersLoadedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? SensitivePurgedAt { get; private set; }
    public List<PortEvent> Events { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public string Reference => $"P-{Number}";

    private PortOrder() { }

    public static PortOrder Create(Guid tenantId, string kind, IEnumerable<string> numbers, string accountType, string endUserName,
        string? currentProviderHint, Guid? preAssignCampaignId, Guid requestedById, string requestedByName, string requestedByEmail,
        string signerEmail, DateTimeOffset now)
    {
        var list = numbers.Distinct().ToList();
        if (list.Count == 0) throw new ArgumentException("An order needs at least one number.");
        if (list.Any(n => PortNumbers.IsTollFree(n) != (kind == PortOrderKind.TollFree)))
            throw new ArgumentException("Toll-free and local numbers go in separate orders.");
        if (string.IsNullOrWhiteSpace(endUserName)) throw new ArgumentException("Enter the business (end user) name.");
        if (!IsEmail(signerEmail)) throw new ArgumentException("Enter the email address of the person who signs for the account.");
        var o = new PortOrder
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Kind = kind, Numbers = list,
            AccountType = accountType == "Residential" ? "Residential" : "Business", EndUserName = endUserName.Trim(),
            CurrentProviderHint = string.IsNullOrWhiteSpace(currentProviderHint) ? null : currentProviderHint.Trim(),
            PreAssignCampaignId = preAssignCampaignId, RequestedById = requestedById, RequestedByName = requestedByName,
            RequestedByEmail = requestedByEmail, SignerEmail = signerEmail.Trim().ToLowerInvariant(), CreatedAt = now, UpdatedAt = now,
        };
        o.Log(now, requestedByName, $"Requested {list.Count} {(kind == PortOrderKind.TollFree ? "toll-free" : "local")} number(s); signing link sent to {o.SignerEmail}");
        return o;
    }

    private static bool IsEmail(string? s) => s is { Length: > 3 and <= 320 } && Regex.IsMatch(s.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

    public void Log(DateTimeOffset at, string by, string text)
    {
        Events = [.. Events, new PortEvent(at, by, text)];
        UpdatedAt = at;
    }

    // ── Signing link ──

    public static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>A fresh signing link (replaces any earlier one). Returns the token for the email — only its hash is kept.</summary>
    public string IssueSigningLink(DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        TokenHash = HashToken(token);
        TokenExpiresAt = now + LinkLifetime;
        return token;
    }

    public bool CanSign(DateTimeOffset now) =>
        Status is PortOrderStatus.AwaitingSignature or PortOrderStatus.NeedsCorrection && TokenExpiresAt is { } e && now < e;

    public void ChangeSignerEmail(string email, string by, DateTimeOffset now)
    {
        if (!IsEmail(email)) throw new ArgumentException("Enter a valid email address.");
        SignerEmail = email.Trim().ToLowerInvariant();
        Log(now, by, $"Signer changed to {SignerEmail}");
    }

    /// <summary>The signer's bill copy (replaces an earlier upload).</summary>
    public void AttachBill(string key, string name, string contentType)
    {
        BillFileKey = key; BillFileName = name; BillContentType = contentType;
    }

    /// <summary>Records the signature. The LOA and certificate are generated from this and attached with <see cref="AttachDocuments"/>.</summary>
    public void Sign(PortSignerDetails details, string? pinProtected, bool pinNotApplicable, string signatureName, string ip, string? userAgent, DateTimeOffset now)
    {
        if (!CanSign(now)) throw new InvalidOperationException("This signing link has expired or was replaced.");
        var problems = details.Problems(Kind);
        if (problems.Count > 0) throw new ArgumentException(problems[0]);
        if (BillFileKey is null) throw new ArgumentException("Upload a recent bill from the current phone company.");
        if (pinProtected is null && !pinNotApplicable) throw new ArgumentException("Enter the account PIN, or tick that the account has none.");
        if (!string.Equals(signatureName.Trim(), details.AuthorizedName.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Type your name exactly as entered for the account owner to sign.");
        Signer = details;
        PinProtected = pinProtected;
        PinNotApplicable = pinNotApplicable;
        SignatureName = signatureName.Trim();
        SignedAt = now; SignerIp = ip; SignerUserAgent = userAgent is { Length: > 300 } ? userAgent[..300] : userAgent;
        TokenHash = null; TokenExpiresAt = null; CorrectionMessage = null;
        Status = PortOrderStatus.ReadyToSubmit;
        Log(now, details.AuthorizedName, "Signed the letter of authorization");
    }

    public void AttachDocuments(string loaKey, string loaSha256, string certificateKey)
    {
        LoaFileKey = loaKey; LoaSha256 = loaSha256; CertificateFileKey = certificateKey;
    }

    /// <summary>Days left before the signature is too old for SignalWire (30 days); null when unsigned.</summary>
    public int? SignatureDaysLeft(DateTimeOffset now) =>
        SignedAt is { } s ? (int)Math.Floor((s + SignatureValidFor - now).TotalDays) : null;

    // ── The porting desk ──

    /// <summary>Sends it back to the signer with a message (a new link; their previous answers are kept to edit).</summary>
    public string RequestCorrection(string message, string by, DateTimeOffset now)
    {
        if (!PortOrderStatus.IsOpen(Status)) throw new InvalidOperationException("This order is closed.");
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Say what needs correcting.");
        CorrectionMessage = message.Trim();
        Status = PortOrderStatus.NeedsCorrection;
        var token = IssueSigningLink(now);
        Log(now, by, $"Sent back for correction: {CorrectionMessage}");
        return token;
    }

    public void MarkSubmitted(string signalWireOrderNumber, string by, DateTimeOffset now)
    {
        if (Status is not (PortOrderStatus.ReadyToSubmit or PortOrderStatus.Submitted))
            throw new InvalidOperationException("Only a signed order can be submitted.");
        if (string.IsNullOrWhiteSpace(signalWireOrderNumber)) throw new ArgumentException("Enter SignalWire's order number.");
        SignalWireOrderNumber = signalWireOrderNumber.Trim();
        Status = PortOrderStatus.Submitted;
        Log(now, by, $"Submitted to SignalWire (order {SignalWireOrderNumber})");
    }

    public void ConfirmFoc(DateOnly date, string by, DateTimeOffset now)
    {
        if (Status is not (PortOrderStatus.Submitted or PortOrderStatus.FocConfirmed))
            throw new InvalidOperationException("Submit the order to SignalWire first.");
        FocDate = date;
        Status = PortOrderStatus.FocConfirmed;
        Log(now, by, $"Port date confirmed: {date:yyyy-MM-dd}");
    }

    public void MarkNumbersLoaded(DateTimeOffset now) => NumbersLoadedAt ??= now;

    public void Complete(string by, DateTimeOffset now)
    {
        if (Status != PortOrderStatus.FocConfirmed) throw new InvalidOperationException("Confirm the port date first.");
        Status = PortOrderStatus.Completed;
        CompletedAt = now;
        Log(now, by, "Port completed");
    }

    public void Cancel(string reason, string by, DateTimeOffset now)
    {
        if (!PortOrderStatus.IsOpen(Status)) throw new InvalidOperationException("This order is already closed.");
        Status = PortOrderStatus.Cancelled;
        CompletedAt = now;
        TokenHash = null; TokenExpiresAt = null;
        Log(now, by, string.IsNullOrWhiteSpace(reason) ? "Cancelled" : $"Cancelled: {reason.Trim()}");
    }

    public void Note(string text, string by, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Write a note.");
        Log(now, by, text.Trim());
    }

    /// <summary>The PIN and bill copy go 30 days after the order closes; returns the bill key to delete.</summary>
    public string? PurgeSensitive(DateTimeOffset now)
    {
        if (CompletedAt is not { } done || now - done < SensitiveRetention || SensitivePurgedAt is not null) return null;
        var bill = BillFileKey;
        PinProtected = null; BillFileKey = null; SensitivePurgedAt = now;
        Log(now, "ContactConnection", "PIN and bill copy deleted (30 days after closing)");
        return bill;
    }
}
