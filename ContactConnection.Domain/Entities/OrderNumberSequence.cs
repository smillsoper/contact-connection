namespace ContactConnection.Domain.Entities;

/// <summary>
/// A per-client order-number generator — e.g. Prefix "LIFSEA-", Width 8, NextValue 10000000 yields
/// "LIFSEA-10000000", "LIFSEA-10000001", ... One number is assigned per call (see
/// CallRecord.OrderNumber) and reused everywhere the order is referenced: the payment gateway's
/// invoice number (Authorize.Net duplicate-transaction detection keys off it), and a client's own
/// order API. Allocation is atomic in the database (see IOrderNumberSequenceRepository), never by
/// read-modify-write of NextValue in memory.
/// </summary>
public class OrderNumberSequence
{
    /// <summary>Authorize.Net caps invoiceNumber and refId at 20 characters — the tightest limit of
    /// any consumer so far, so every generated number must fit within it.</summary>
    public const int MaxFormattedLength = 20;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid ClientId { get; private set; }
    public string Prefix { get; private set; } = "";
    public string Suffix { get; private set; } = "";
    /// <summary>Minimum digit count — the number is left-padded with zeros to this width.</summary>
    public int Width { get; private set; }
    /// <summary>The value the next allocation will use.</summary>
    public long NextValue { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OrderNumberSequence() { }

    public static OrderNumberSequence Create(
        Guid tenantId, Guid clientId, string prefix, string suffix, int width, long nextValue)
    {
        Validate(prefix, suffix, width, nextValue);

        var now = DateTimeOffset.UtcNow;
        return new OrderNumberSequence
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            ClientId = clientId,
            Prefix = prefix,
            Suffix = suffix,
            Width = width,
            NextValue = nextValue,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Changes the format and, optionally, the next value. Lowering NextValue is allowed
    /// (an admin may be correcting a mistake) but risks reissuing numbers already used — the admin
    /// UI warns about it; the domain doesn't forbid it.</summary>
    public void Update(string prefix, string suffix, int width, long nextValue)
    {
        Validate(prefix, suffix, width, nextValue);
        Prefix = prefix;
        Suffix = suffix;
        Width = width;
        NextValue = nextValue;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public string Format(long value) => Format(Prefix, Suffix, Width, value);

    public static string Format(string prefix, string suffix, int width, long value)
        => $"{prefix}{value.ToString().PadLeft(width, '0')}{suffix}";

    private static void Validate(string prefix, string suffix, int width, long nextValue)
    {
        if (width is < 1 or > 18)
            throw new ArgumentException("Width must be between 1 and 18 digits.", nameof(width));
        if (nextValue < 0)
            throw new ArgumentException("Next value cannot be negative.", nameof(nextValue));
        if (prefix.Any(char.IsWhiteSpace) || suffix.Any(char.IsWhiteSpace))
            throw new ArgumentException("Prefix and suffix cannot contain whitespace.");

        // Check against the widest number this sequence could realistically produce: the larger of
        // the padded width and the next value's own digit count.
        var digits = Math.Max(width, nextValue.ToString().Length);
        if (prefix.Length + digits + suffix.Length > MaxFormattedLength)
            throw new ArgumentException(
                $"Order numbers would exceed {MaxFormattedLength} characters " +
                $"(prefix + {digits} digits + suffix) — payment gateways reject longer invoice numbers.");
    }
}
