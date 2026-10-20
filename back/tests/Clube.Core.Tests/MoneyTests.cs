using Clube.Core.Domain.Ledger;

namespace Clube.Core.Tests;

public class MoneyTests
{
    [Theory]
    [InlineData(2990, "0.15", 449)] // 448,5 centavos: AwayFromZero sobe, ToEven desceria para 448
    [InlineData(5, "0.5", 3)]       // 2,5 centavos: ToEven daria 2
    [InlineData(1250, "0.10", 125)]
    [InlineData(1999, "0.0", 0)]
    public void RoundsPercentagesCommercially(long cents, string rate, long expectedCents) =>
        Assert.Equal(expectedCents, Money.FromCents(cents).PercentOf(decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture)).Cents);

    [Fact]
    public void ThrowsInsteadOfWrappingOnOverflow() =>
        Assert.Throws<OverflowException>(() => Money.FromCents(long.MaxValue).Add(Money.FromCents(1)));

    [Fact]
    public void FormatsAsBrazilianReais()
    {
        string formatted = Money.FromCents(123456).ToString();

        // pt-BR separates the symbol with a non-breaking space; compare the parts that matter.
        Assert.StartsWith("R$", formatted);
        Assert.EndsWith("1.234,56", formatted);
    }
}
