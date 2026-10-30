using System.Security.Cryptography;
using System.Text;
using Lab.Contracts.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Lab.Infrastructure.DependencyInjection;
using Lab.Infrastructure.Configuration;

namespace Lab.Client.Maui.Services;

/// <summary>Ізоляція кешу за сервером та користувачем; токен зберігається платформою окремо.</summary>
public sealed record ClientAccount(string Server, int UserId, string Name, string Email, string Token, DateTime ExpiresAtUtc)
{
    public static Uri ValidateServer(string address)
    {
        if (!Uri.TryCreate(address.TrimEnd('/') + "/", UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("Потрібна HTTPS-адреса сервера без шляху та облікових даних.", nameof(address));
        return uri;
    }

    public string WorkspacePath(string root)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ValidateServer(Server).AbsoluteUri)));
        return Path.Combine(root, hash, UserId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static ClientAccount FromToken(Uri server, string email, AccessTokenResponse response) =>
        new(server.AbsoluteUri, response.UserId, response.Name, email, response.AccessToken, response.ExpiresAtUtc);

    public ServiceProvider OpenServices(string root)
    {
        // Клієнт ніколи не повинен підключитися до серверної БД через залишене середовище.
        if (string.Equals(Environment.GetEnvironmentVariable(DatabaseOptions.ProviderEnvironmentVariable), "Postgres", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Приберіть LAB_DB_PROVIDER=Postgres перед запуском клієнта.");
        var directory = WorkspacePath(root);
        Directory.CreateDirectory(directory);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite",
            ["ConnectionStrings:Sqlite"] = $"Data Source={Path.Combine(directory, "lab.sqlite3")};Pooling=False",
        }).Build();
        return new ServiceCollection().AddLabInfrastructure(configuration).BuildServiceProvider();
    }
}
