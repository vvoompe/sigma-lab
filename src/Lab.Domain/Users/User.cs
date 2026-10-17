using System.Net.Mail;
using Lab.Domain.Validation;

namespace Lab.Domain.Users;

/// <summary>Профіль. Паролі, ролі, токени та автентифікація належать Z3.</summary>
public sealed class User
{
    private User() { }

    public User(string name, string email)
    {
        Name = DomainRules.Text(name, nameof(name));
        Email = DomainRules.Text(email, nameof(email), 254);
        if (!MailAddress.TryCreate(Email, out var address) || address.Address != Email)
        {
            throw new ArgumentException("Некоректна email-адреса.", nameof(email));
        }

        NormalizedEmail = Email.ToUpperInvariant();
    }

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;
}
