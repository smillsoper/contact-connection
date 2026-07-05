using ContactConnection.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ContactConnection.Infrastructure.Persistence.Configurations;

public class AudioFileConfiguration : IEntityTypeConfiguration<AudioFile>
{
    public void Configure(EntityTypeBuilder<AudioFile> b)
    {
        b.ToTable("audio_files");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TenantId).HasColumnName("tenant_id");
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(255).IsRequired();
        b.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(512).IsRequired();
        b.Property(x => x.StoredFileName).HasColumnName("stored_file_name").HasMaxLength(255).IsRequired();
        b.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(127).IsRequired();
        b.Property(x => x.FileSizeBytes).HasColumnName("file_size_bytes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.HasIndex(x => x.TenantId).HasDatabaseName("ix_audio_files_tenant_id");
    }
}
