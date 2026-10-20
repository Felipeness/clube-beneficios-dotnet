using System.Globalization;

namespace Clube.Core.Domain.Ledger;

// Centavos in a long: exact, and identical in the database (bigint), in JSON and in the JavaScript front end,
// where a decimal would silently become a double.
public readonly record struct Money
{
    private static readonly CultureInfo Brazil = CultureInfo.GetCultureInfo("pt-BR");

    public static readonly Money Zero = new(0);

    public long Cents { get; }

    private Money(long cents) => Cents = cents;

    public static Money FromCents(long cents) => new(cents);

    public bool IsPositive => Cents > 0;

    public Money Add(Money other) => new(checked(Cents + other.Cents));

    public Money Subtract(Money other) => new(checked(Cents - other.Cents));

    // Math.Round defaults to banker's rounding (ToEven), which moves centavos at the midpoint;
    // Brazilian commerce expects AwayFromZero.
    public Money PercentOf(decimal rate) => new((long)Math.Round(Cents * rate, MidpointRounding.AwayFromZero));

    public static bool operator <(Money left, Money right) => left.Cents < right.Cents;

    public static bool operator >(Money left, Money right) => left.Cents > right.Cents;

    public override string ToString() => (Cents / 100m).ToString("C", Brazil);
}
