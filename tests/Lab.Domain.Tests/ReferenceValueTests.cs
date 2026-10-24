using Lab.Domain.ReferenceValues;

namespace Lab.Domain.Tests;

/// <summary>Правила еталона: назва, одиниця, скінченне значення й невід'ємний допуск.</summary>
public sealed class ReferenceValueTests
{
    [Fact]
    public void Створення_з_коректними_значеннями_працює()
    {
        var value = new ReferenceValue("Еталон pH", "pH", 7.4, 0.05);

        Assert.Equal("Еталон pH", value.Name);
        Assert.Equal("pH", value.Unit);
        Assert.Equal(7.4, value.Value);
        Assert.Equal(0.05, value.AbsoluteTolerance);
    }

    [Fact]
    public void Оновлення_змінює_всі_поля()
    {
        var value = new ReferenceValue("Еталон", "pH", 7.4, 0.05);

        value.Update("Еталон 2", "C", 12.5, 0.2);

        Assert.Equal("Еталон 2", value.Name);
        Assert.Equal("C", value.Unit);
        Assert.Equal(12.5, value.Value);
        Assert.Equal(0.2, value.AbsoluteTolerance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Порожня_назва_відхиляється(string name) =>
        Assert.Throws<ArgumentException>(() => new ReferenceValue(name, "pH", 7.4, 0.05));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Порожня_одиниця_відхиляється(string unit) =>
        Assert.Throws<ArgumentException>(() => new ReferenceValue("Еталон", unit, 7.4, 0.05));

    [Fact]
    public void Відємний_допуск_відхиляється() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceValue("Еталон", "pH", 7.4, -0.1));

    [Fact]
    public void Нескінченне_значення_відхиляється() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new ReferenceValue("Еталон", "pH", double.NaN, 0.1));

    [Fact]
    public void Оновлення_з_некоректним_допуском_не_змінює_стан()
    {
        var value = new ReferenceValue("Еталон", "pH", 7.4, 0.05);

        Assert.Throws<ArgumentOutOfRangeException>(() => value.Update("Новий", "pH", 7.0, -1));

        Assert.Equal("Еталон", value.Name);
        Assert.Equal(7.4, value.Value);
        Assert.Equal(0.05, value.AbsoluteTolerance);
    }
}
