using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Data.Configurations;

// Agent help desks (S184). Id lists are native uuid[] so "help desks for this campaign" is = ANY(...) in SQL.

public class HelpdeskConfiguration : IEntityTypeConfiguration<Helpdesk>
{
    public void Configure(EntityTypeBuilder<Helpdesk> b)
    {
        b.ToTable("helpdesks");
        b.HasKey(h => h.Id);
        b.Property(h => h.Id).HasColumnName("id");
        b.Property(h => h.TenantId).HasColumnName("tenant_id");
        b.Property(h => h.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        b.Property(h => h.Description).HasColumnName("description").HasMaxLength(300);
        b.Property(h => h.CampaignIds).HasColumnName("campaign_ids").HasColumnType("uuid[]");
        b.Property(h => h.IsActive).HasColumnName("is_active");
        b.Property(h => h.CreatedAt).HasColumnName("created_at");
        b.Property(h => h.UpdatedAt).HasColumnName("updated_at");
    }
}

public class HelpdeskTopicConfiguration : IEntityTypeConfiguration<HelpdeskTopic>
{
    public void Configure(EntityTypeBuilder<HelpdeskTopic> b)
    {
        b.ToTable("helpdesk_topics");
        b.HasKey(t => t.Id);
        b.Property(t => t.Id).HasColumnName("id");
        b.Property(t => t.HelpdeskId).HasColumnName("helpdesk_id");
        b.Property(t => t.Title).HasColumnName("title").HasMaxLength(HelpdeskTopic.MaxTitle).IsRequired();
        b.Property(t => t.Html).HasColumnName("html").IsRequired();
        b.Property(t => t.Text).HasColumnName("text").IsRequired();
        b.Property(t => t.AttachmentIds).HasColumnName("attachment_ids").HasColumnType("uuid[]");
        b.Property(t => t.SortOrder).HasColumnName("sort_order");
        b.Property(t => t.CreatedAt).HasColumnName("created_at");
        b.Property(t => t.UpdatedAt).HasColumnName("updated_at");
        b.Property(t => t.UpdatedByName).HasColumnName("updated_by_name").HasMaxLength(200).IsRequired();
        b.HasIndex(t => new { t.HelpdeskId, t.SortOrder }).HasDatabaseName("ix_helpdesk_topics_helpdesk_order");
    }
}

public class HelpdeskFileConfiguration : IEntityTypeConfiguration<HelpdeskFile>
{
    public void Configure(EntityTypeBuilder<HelpdeskFile> b)
    {
        b.ToTable("helpdesk_files");
        b.HasKey(f => f.Id);
        b.Property(f => f.Id).HasColumnName("id");
        b.Property(f => f.TenantId).HasColumnName("tenant_id");
        b.Property(f => f.HelpdeskId).HasColumnName("helpdesk_id");
        b.Property(f => f.IsImage).HasColumnName("is_image");
        b.Property(f => f.FileName).HasColumnName("file_name").HasMaxLength(200).IsRequired();
        b.Property(f => f.ContentType).HasColumnName("content_type").HasMaxLength(120).IsRequired();
        b.Property(f => f.SizeBytes).HasColumnName("size_bytes");
        b.Property(f => f.StorageKey).HasColumnName("storage_key").HasMaxLength(200).IsRequired();
        b.Property(f => f.UploadedById).HasColumnName("uploaded_by_id");
        b.Property(f => f.CreatedAt).HasColumnName("created_at");
        b.HasIndex(f => f.HelpdeskId).HasDatabaseName("ix_helpdesk_files_helpdesk");
    }
}
