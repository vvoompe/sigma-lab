namespace Lab.Domain.Validation;

internal static class DomainRules
{
    internal static string Text(string value, string parameter, int maximum = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        var result = value.Trim();
        if (result.Length > maximum)
        {
            throw new ArgumentException($"Довжина не має перевищувати {maximum} символів.", parameter);
        }

        return result;
    }

    internal static void PositiveId(int value, string parameter) => ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value, parameter);

    internal static void Finite(double value, string parameter)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentOutOfRangeException(parameter, "Потрібне скінченне число.");
        }
    }

    internal static void Utc(DateTime value, string parameter)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("Час має бути явно заданий у UTC.", parameter);
        }
    }
}
