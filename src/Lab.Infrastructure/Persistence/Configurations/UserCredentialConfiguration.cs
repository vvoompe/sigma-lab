using Lab.Domain.Security;
using Lab.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

public sealed class UserCredentialConfiguration : IEntityTypeConfiguration<UserCredential>
{
    public void Configure(EntityTypeBuilder<UserCredential> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("user_credentials", table =>
            table.HasCheckConstraint("ck_user_credentials_failed_attempts", "\"FailedAttempts\" >= 0"));
        builder.HasKey(credential => credential.UserId);
        builder.Property(credential => credential.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(credential => credential.PasswordUpdatedAt)
            .HasConversion(value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc))
            .IsRequired();
        builder.Property(credential => credential.LockoutUntil)
            .HasConversion(
                value => value,
                value => value == null ? null : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));
        builder.Property(credential => credential.Role)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();
        builder.HasOne(credential => credential.User)
            .WithOne()
            .HasForeignKey<UserCredential>(credential => credential.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
