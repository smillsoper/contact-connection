namespace ContactConnection.Domain.Entities;

/// <summary>
/// An agent help desk (S184): reference articles ("topics") an admin writes for one or more campaigns — offers,
/// policies, rebuttals, how-tos. On a call, the agent opens the help desks for that call's campaign (a tab each).
/// </summary>
public class Helpdesk
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = "";
    public string? Description { get; private set; }
    /// <summary>The campaigns whose agents see it.</summary>
    public List<Guid> CampaignIds { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Helpdesk() { }

    public static Helpdesk Create(Guid tenantId, string name, string? description, IEnumerable<Guid> campaignIds)
    {
        var h = new Helpdesk { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = DateTimeOffset.UtcNow };
        h.Update(name, description, campaignIds, true);
        return h;
    }

    public void Update(string name, string? description, IEnumerable<Guid> campaignIds, bool isActive)
    {
        var n = (name ?? "").Trim();
        if (n.Length == 0) throw new ArgumentException("Give the help desk a name.");
        if (n.Length > 80) throw new ArgumentException("Keep the name under 80 characters.");
        Name = n;
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim()[..Math.Min(description.Trim().Length, 300)];
        CampaignIds = campaignIds.Distinct().ToList();
        IsActive = isActive;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

/// <summary>One article in a help desk: a title, formatted content (embedded images reference uploaded files) and
/// attached files.</summary>
public class HelpdeskTopic
{
    public const int MaxTitle = 150;
    public const int MaxAttachments = 20;

    public Guid Id { get; private set; }
    public Guid HelpdeskId { get; private set; }
    public string Title { get; private set; } = "";
    /// <summary>Sanitized HTML.</summary>
    public string Html { get; private set; } = "";
    /// <summary>The words, for searching.</summary>
    public string Text { get; private set; } = "";
    public List<Guid> AttachmentIds { get; private set; } = [];
    public int SortOrder { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string UpdatedByName { get; private set; } = "";

    private HelpdeskTopic() { }

    public static HelpdeskTopic Create(Guid helpdeskId, int sortOrder) =>
        new() { Id = Guid.NewGuid(), HelpdeskId = helpdeskId, SortOrder = sortOrder, CreatedAt = DateTimeOffset.UtcNow };

    public void Update(string title, string html, string text, IEnumerable<Guid> attachmentIds, string byName)
    {
        var t = (title ?? "").Trim();
        if (t.Length == 0) throw new ArgumentException("Give the topic a title.");
        if (t.Length > MaxTitle) throw new ArgumentException($"Keep the title under {MaxTitle} characters.");
        var files = attachmentIds.Distinct().ToList();
        if (files.Count > MaxAttachments) throw new ArgumentException($"At most {MaxAttachments} attached files per topic.");
        Title = t; Html = html; Text = text; AttachmentIds = files;
        UpdatedAt = DateTimeOffset.UtcNow; UpdatedByName = byName;
    }

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;
}

/// <summary>An uploaded help desk file: an image embedded in a topic, or an attachment.</summary>
public class HelpdeskFile
{
    public const long MaxImageBytes = 5 * 1024 * 1024;
    public const long MaxFileBytes = 25 * 1024 * 1024;

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid HelpdeskId { get; private set; }
    public bool IsImage { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public string StorageKey { get; private set; } = "";
    public Guid UploadedById { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private HelpdeskFile() { }

    public static HelpdeskFile Create(Guid tenantId, Guid helpdeskId, bool isImage, string fileName, string contentType, long size, Guid uploadedById)
    {
        var id = Guid.NewGuid();
        return new HelpdeskFile
        {
            Id = id, TenantId = tenantId, HelpdeskId = helpdeskId, IsImage = isImage, FileName = fileName, ContentType = contentType,
            SizeBytes = size, StorageKey = $"helpdesk/{tenantId}/{helpdeskId}/{id}", UploadedById = uploadedById, CreatedAt = DateTimeOffset.UtcNow,
        };
    }
}
