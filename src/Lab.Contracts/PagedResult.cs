namespace Lab.Contracts;

/// <summary>Сторінка колекції та кількість записів після фільтрації.</summary>
/// <typeparam name="T">Контракт елемента.</typeparam>
/// <param name="Items">Елементи поточної сторінки.</param>
/// <param name="TotalCount">Кількість усіх записів, що відповідають фільтрам.</param>
/// <param name="Page">Номер сторінки, починаючи з одиниці.</param>
/// <param name="PageSize">Максимальна кількість елементів на сторінці.</param>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
