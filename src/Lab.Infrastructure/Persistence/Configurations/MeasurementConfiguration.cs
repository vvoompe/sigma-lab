using Lab.Domain.Measurements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class MeasurementConfiguration : IEntityTypeConfiguration<Measurement>
{
    public void Configure(EntityTypeBuilder<Measurement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("measurements", table => table.HasCheckConstraint("ck_measurements_version", "\"Version\" > 0"));
        builder.HasKey(item => item.Id);
        builder.HasIndex(item => item.ClientId).IsUnique();
        builder.HasIndex(item => new { item.ExperimentId, item.MeasuredAt });
        builder.Property(item => item.Version).IsConcurrencyToken();
        builder.Property(item => item.MeasuredAt).HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));
    }
}
