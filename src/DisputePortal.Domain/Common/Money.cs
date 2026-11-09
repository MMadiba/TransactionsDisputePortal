using System.Globalization;

namespace DisputePortal.Domain.Common;

/// <summary>
/// Value object for an amount of money. Stored in minor units (cents) so it never passes through
/// floating point. Negative amounts are debits (money leaving the account), positive are credits.
/// </summary>
public sealed record Money
{
    public Money(long amountMinor, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Length != 3 || !currency.All(char.IsAsciiLetter))
        {
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code.", nameof(currency));
        }

        AmountMinor = amountMinor;
        Currency = currency.ToUpperInvariant();
    }

    public long AmountMinor { get; private init; }

    public string Currency { get; private init; }

    public decimal Amount => AmountMinor / 100m;

    public bool IsDebit => AmountMinor < 0;

    public bool IsCredit => AmountMinor > 0;

    public static Money FromMajor(decimal amount, string currency) =>
        new(decimal.ToInt64(decimal.Round(amount * 100m, MidpointRounding.ToEven)), currency);

    /// <summary>e.g. "-ZAR 1,250.00". Invariant culture keeps the output identical on every host.</summary>
    public override string ToString() => Format(Amount, Currency);

    public static string Format(decimal amount, string currency) =>
        (amount < 0 ? "-" : "") + currency + " " + Math.Abs(amount).ToString("N2", CultureInfo.InvariantCulture);
}
