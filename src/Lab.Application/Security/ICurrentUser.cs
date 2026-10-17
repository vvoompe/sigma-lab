namespace Lab.Application.Security;

/// <summary>Ідентичність і роль користувача з перевіреного запиту.</summary>
public interface ICurrentUser
{
    /// <summary>Ідентифікатор користувача або null для анонімного запиту.</summary>
    int? Id { get; }

    /// <summary>Роль користувача або null, якщо вона відсутня.</summary>
    string? Role { get; }
}
