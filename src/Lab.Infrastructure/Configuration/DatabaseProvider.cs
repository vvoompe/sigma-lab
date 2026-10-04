namespace Lab.Infrastructure.Configuration;

/// <summary>Підтримувані СУБД.</summary>
public enum DatabaseProvider
{
    /// <summary>SQLite - використовується клієнтом і локальною розробкою.</summary>
    Sqlite = 0,

    /// <summary>PostgreSQL - основна СУБД сервера.</summary>
    Postgres = 1,
}
