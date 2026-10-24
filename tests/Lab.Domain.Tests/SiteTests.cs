using Lab.Domain.Sites;

namespace Lab.Domain.Tests;

/// <summary>
/// Тести правил домену. Це найдешевші тести в проєкті: без бази даних, без HTTP,
/// виконуються за мілісекунди - саме тому правила предметної області тримаємо в домені.
/// </summary>
public sealed class SiteTests
{
    [Theory]
    [InlineData("Лісова ділянка")]
    [InlineData("abc")]
    [InlineData("  Майданчик з пробілами по краях  ")]
    public void Назва_в_межах_довжини_приймається(string name) =>
        Assert.True(Site.IsNameValid(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData(null)]
    public void Назва_порожня_або_коротка_відхиляється(string? name) =>
        Assert.False(Site.IsNameValid(name));

    [Fact]
    public void Назва_довша_за_максимум_відхиляється() =>
        Assert.False(Site.IsNameValid(new string('я', Site.MaxNameLength + 1)));

    [Theory]
    [InlineData(-90d)]
    [InlineData(0d)]
    [InlineData(50.45d)]
    [InlineData(90d)]
    public void Широта_в_межах_приймається(double latitude) =>
        Assert.True(Site.IsLatitudeInRange(latitude));

    [Theory]
    [InlineData(-90.1d)]
    [InlineData(90.1d)]
    [InlineData(double.NaN)]
    public void Широта_поза_межами_відхиляється(double latitude) =>
        Assert.False(Site.IsLatitudeInRange(latitude));

    [Theory]
    [InlineData(-180d)]
    [InlineData(30.52d)]
    [InlineData(180d)]
    public void Довгота_в_межах_приймається(double longitude) =>
        Assert.True(Site.IsLongitudeInRange(longitude));

    [Theory]
    [InlineData(-180.1d)]
    [InlineData(180.1d)]
    [InlineData(double.NaN)]
    public void Довгота_поза_межами_відхиляється(double longitude) =>
        Assert.False(Site.IsLongitudeInRange(longitude));

    [Fact]
    public void Новий_майданчик_має_порожню_назву_і_час_створення()
    {
        var site = new Site();

        Assert.Equal(string.Empty, site.Name);
        Assert.True(site.CreatedAt <= DateTime.UtcNow);
    }

    [Fact]
    public void Властивості_сутності_зберігають_присвоєні_значення()
    {
        var createdAt = new DateTime(2026, 9, 29, 10, 0, 0, DateTimeKind.Utc);

        var site = new Site
        {
            Id = 7,
            Name = "Лісова ділянка",
            Latitude = 50.45,
            Longitude = 30.52,
            OwnerId = 3,
            CreatedAt = createdAt,
        };

        Assert.Equal(7, site.Id);
        Assert.Equal("Лісова ділянка", site.Name);
        Assert.Equal(50.45, site.Latitude);
        Assert.Equal(30.52, site.Longitude);
        Assert.Equal(3, site.OwnerId);
        Assert.Equal(createdAt, site.CreatedAt);
    }

    [Fact]
    public void Новий_майданчик_не_має_призначеного_менеджера() =>
        Assert.Null(new Site { Name = "Лісова ділянка" }.ManagerId);

    [Fact]
    public void Призначення_менеджера_змінює_стан_і_повертає_ознаку_зміни()
    {
        var site = new Site { Name = "Лісова ділянка" };

        var changed = site.AssignManager(12);

        Assert.True(changed);
        Assert.Equal(12, site.ManagerId);
    }

    [Fact]
    public void Повторне_призначення_того_самого_менеджера_не_є_зміною()
    {
        var site = new Site { Name = "Лісова ділянка" };
        site.AssignManager(12);

        var changed = site.AssignManager(12);

        Assert.False(changed);
        Assert.Equal(12, site.ManagerId);
    }

    [Fact]
    public void Призначення_менеджера_не_змінює_власника()
    {
        var site = new Site { Name = "Лісова ділянка", OwnerId = 3 };

        site.AssignManager(12);

        Assert.Equal(3, site.OwnerId);
        Assert.Equal(12, site.ManagerId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Неприпустимий_ідентифікатор_менеджера_відхиляється(int managerId)
    {
        var site = new Site { Name = "Лісова ділянка" };

        Assert.Throws<ArgumentOutOfRangeException>(() => site.AssignManager(managerId));
        Assert.Null(site.ManagerId);
    }
}
