using System.Reflection;
using Lab.Domain.Sites;

namespace Lab.Domain.Tests;

/// <summary>
/// Тест архітектури: домен не має залежати від інфраструктурних бібліотек.
/// Якщо хтось додасть EF-атрибут у сутність або посилання на ASP.NET, цей тест упаде -
/// і це дешевше, ніж виявити це на захисті.
/// </summary>
public sealed class DependencyTests
{
    private static readonly string[] ForbiddenPrefixes =
    [
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Npgsql",
        "Microsoft.Data.Sqlite",
        "Microsoft.Extensions.DependencyInjection",
    ];

    [Fact]
    public void Домен_не_залежить_від_інфраструктурних_бібліотек()
    {
        var referenced = typeof(Site).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name ?? string.Empty)
            .ToArray();

        var violations = referenced
            .Where(name => ForbiddenPrefixes.Any(prefix =>
                name.StartsWith(prefix, StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Домен_містить_сутності_у_просторі_імен_предметної_області()
    {
        var siteType = typeof(Site);

        Assert.Equal("Lab.Domain.Sites", siteType.Namespace);
        Assert.False(siteType.IsAbstract);
        Assert.True(siteType.IsSealed, "Сутність оголошуємо sealed: її не призначено для наслідування.");
    }
}
