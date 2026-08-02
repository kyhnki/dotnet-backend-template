using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DotNetBackendTemplate.Api.Persistence.Auditing;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");
        builder.HasKey(auditLog => auditLog.Id);

        builder.Property(auditLog => auditLog.Action)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(auditLog => auditLog.EntityName)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(auditLog => auditLog.EntityId)
            .HasMaxLength(512)
            .IsRequired();

        builder.Property(auditLog => auditLog.UserId)
            .HasMaxLength(256);

        builder.Property(auditLog => auditLog.TraceId)
            .HasMaxLength(128);

        builder.Property(auditLog => auditLog.RequestPath)
            .HasMaxLength(2048);

        builder.Property(auditLog => auditLog.OldValuesJson)
            .HasColumnType("nvarchar(max)");

        builder.Property(auditLog => auditLog.NewValuesJson)
            .HasColumnType("nvarchar(max)");

        builder.HasIndex(auditLog => auditLog.OccurredAtUtc);
        builder.HasIndex(auditLog => new { auditLog.EntityName, auditLog.EntityId });
        builder.HasIndex(auditLog => new { auditLog.UserId, auditLog.OccurredAtUtc });
    }
}
