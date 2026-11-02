namespace Lab.Application.Security;

public interface IInitialAdministratorSeeder
{
    Task SeedAsync(string? email, string? password, CancellationToken cancellationToken = default);
}
