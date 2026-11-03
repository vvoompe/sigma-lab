using System.Text.Json;
using Lab.Client.Maui.ViewModels;

namespace Lab.Client.Maui.Services;

/// <summary>Токен і профіль зберігаються у платформному SecureStorage; пароль не зберігається.</summary>
public sealed class SecureClientSessionStore : IClientSessionStore
{
    private const string Key = "sigma.active-account.v1";
    public async Task<ClientAccount?> LoadAsync()
    {
        var value = await SecureStorage.Default.GetAsync(Key);
        return value is null ? null : JsonSerializer.Deserialize<ClientAccount>(value);
    }
    public Task SaveAsync(ClientAccount account) => SecureStorage.Default.SetAsync(Key, JsonSerializer.Serialize(account));
    public void Clear() => SecureStorage.Default.Remove(Key);
}
