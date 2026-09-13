namespace Ledgerly.Domain.Wallets;

public sealed record Currency
{
    private Currency(string code)
    {
        Code = code;
    }

    public string Code { get; }

    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Currency code cannot be empty.", nameof(code));
        }

        var normalizedCode = code.Trim().ToUpperInvariant();

        if (normalizedCode != "TRY")
        {
            throw new ArgumentException(
                $"Currency code '{normalizedCode}' is not supported.",
                nameof(code)
            );
        }

        return new Currency(normalizedCode);
    }
}
