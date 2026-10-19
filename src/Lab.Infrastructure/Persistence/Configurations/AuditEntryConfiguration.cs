using Lab.Domain.Audit;
using Lab.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("audit_entries");
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedOnAdd();
        builder.Property(entry => entry.Action).HasMaxLength(AuditEntry.MaxActionLength).IsRequired();
        builder.Property(entry => entry.TargetType).HasMaxLength(AuditEntry.MaxTargetTypeLength).IsRequired();
        builder.Property(entry => entry.TargetId).IsRequired();
        builder.Property(entry => entry.OccurredAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(entry => entry.Details).HasMaxLength(AuditEntry.MaxDetailsLength);
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(entry => entry.ActorId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(entry => new { entry.ActorId, entry.OccurredAt });
        builder.HasIndex(entry => new { entry.TargetType, entry.TargetId, entry.OccurredAt });
    }
}
