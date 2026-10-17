using Lab.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("lab_users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Name).HasMaxLength(100).IsRequired();
        builder.Property(user => user.Email).HasMaxLength(254).IsRequired();
        builder.Property(user => user.NormalizedEmail).HasMaxLength(254).IsRequired();
        builder.HasIndex(user => user.NormalizedEmail).IsUnique();
    }
}
