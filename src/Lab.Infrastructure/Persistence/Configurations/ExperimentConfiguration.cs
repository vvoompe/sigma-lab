using Lab.Domain.Experiments;
using Lab.Domain.Sites;
using Lab.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class ExperimentConfiguration : IEntityTypeConfiguration<Experiment>
{
    public void Configure(EntityTypeBuilder<Experiment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("experiments", table =>
        {
            table.HasCheckConstraint("ck_experiments_version", "\"Version\" > 0");
            table.HasCheckConstraint("ck_experiments_signature", "(\"Status\" = 0 AND \"SignedAt\" IS NULL AND \"SignedById\" IS NULL) OR (\"Status\" = 1 AND \"SignedAt\" IS NOT NULL AND \"SignedById\" IS NOT NULL)");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.Property(item => item.CreatedAt).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
        builder.Property(item => item.SignedAt).HasConversion(value => value, value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        builder.Ignore(item => item.Unit);
        builder.HasIndex(item => new { item.SiteId, item.Status });
        builder.HasIndex(item => new { item.OwnerId, item.CreatedAt });
        // Батьківські ресурси й використаний еталон не видаляються каскадно.
        builder.HasOne<Site>().WithMany().HasForeignKey(item => item.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.SignedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(item => item.Instrument).WithMany().HasForeignKey(item => item.InstrumentId).OnDelete(DeleteBehavior.Restrict).IsRequired();
        builder.HasOne(item => item.ReferenceValue).WithMany().HasForeignKey(item => item.ReferenceValueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(item => item.Measurements).WithOne(item => item.Experiment).HasForeignKey(item => item.ExperimentId).OnDelete(DeleteBehavior.Cascade).IsRequired();
        builder.Navigation(item => item.Measurements).HasField("measurements").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
