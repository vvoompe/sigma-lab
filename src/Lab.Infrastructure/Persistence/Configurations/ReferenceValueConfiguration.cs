using Lab.Domain.ReferenceValues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class ReferenceValueConfiguration : IEntityTypeConfiguration<ReferenceValue>
{
    public void Configure(EntityTypeBuilder<ReferenceValue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("reference_values", table => table.HasCheckConstraint("ck_reference_tolerance", "\"AbsoluteTolerance\" >= 0"));
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Unit).HasMaxLength(32).IsRequired();
    }
}
