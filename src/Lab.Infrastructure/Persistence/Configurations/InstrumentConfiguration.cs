using Lab.Domain.Instruments;
using Lab.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("instruments", table =>
        {
            table.HasCheckConstraint("ck_instruments_range", "\"MinValue\" < \"MaxValue\"");
            table.HasCheckConstraint("ck_instruments_error", "\"AbsoluteError\" >= 0");
        });
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name).HasMaxLength(100).IsRequired();
        builder.Property(item => item.SerialNumber).HasMaxLength(100).IsRequired();
        builder.Property(item => item.Unit).HasMaxLength(32).IsRequired();
        builder.HasIndex(item => item.SerialNumber).IsUnique();
        // Видалення власника не має знищувати прилад і історію вимірів.
        builder.HasOne<User>().WithMany().HasForeignKey(item => item.OwnerId).OnDelete(DeleteBehavior.Restrict);
    }
}
