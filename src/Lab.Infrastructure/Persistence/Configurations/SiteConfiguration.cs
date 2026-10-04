using Lab.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lab.Infrastructure.Persistence.Configurations;

/// <summary>
/// Fluent-конфігурація майданчика: назви таблиць і колонок, обмеження, індекси та
/// політика видалення задаються тут, а не атрибутами в домені.
/// </summary>
public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("sites");
        builder.HasKey(site => site.Id);

        builder.Property(site => site.Id).ValueGeneratedOnAdd();
        builder.Property(site => site.Name)
            .HasMaxLength(Site.MaxNameLength)
            .IsRequired();
        builder.Property(site => site.Latitude).IsRequired();
        builder.Property(site => site.Longitude).IsRequired();
        builder.Property(site => site.OwnerId).IsRequired();
        builder.Property(site => site.CreatedAt).IsRequired();

        // Індекси під реальні запити: пошук і сортування за назвою, пошук за координатами.
        builder.HasIndex(site => site.Name);
        builder.HasIndex(site => new { site.Latitude, site.Longitude });
    }
}
