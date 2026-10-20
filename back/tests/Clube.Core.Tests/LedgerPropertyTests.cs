using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using CsCheck;

namespace Clube.Core.Tests;

// Property-based tests: instead of hand-picked examples, CsCheck builds random sequences of commands against the hexagon
// and shrinks a failing one to the smallest that still breaks the rule. 100 sequences per property by default;
// CsCheck_Iter=10000 (or CsCheck_Seed=<seed printed by a failure> to replay one) changes that without touching the code.
public class LedgerPropertyTests
{
    private const long MaxCents = 100_000_000_000;

    [Fact]
    public Task KeepsEveryTransactionAndTheWholeLedgerBalancedAtZero() =>
        CommandGenerators.Scripts.SampleAsync(async script =>
        {
            var world = await LedgerWorld.RunAsync(script);

            Assert.All(world.Posted, transaction => Assert.Equal(0, transaction.Entries.Sum(entry => entry.SignedCents)));
            Assert.Equal(0, world.Posted.Sum(transaction => transaction.Entries.Sum(entry => entry.SignedCents)));
        });

    [Fact]
    public Task EqualsTheBalanceOfAMemberToTheSumOfTheWholeStatement() =>
        CommandGenerators.Scripts.SampleAsync(async script =>
        {
            var world = await LedgerWorld.RunAsync(script);

            for (int member = 0; member < world.MemberCount; member++)
                Assert.Equal(await world.BalanceAsync(member), (await world.StatementAsync(member)).Sum(line => line.AmountCents));
        });

    [Fact]
    public Task GoesNegativeOnlyThroughAReversedCreditAndNeverThroughASpend() =>
        CommandGenerators.Scripts.SampleAsync(async script =>
        {
            var world = await LedgerWorld.RunAsync(script);

            for (int member = 0; member < world.MemberCount; member++)
                AssertDebtComesOnlyFromReversals(await world.StatementAsync(member));
        });

    [Fact]
    public Task ChangesNothingWhenAnIdempotentCommandIsSentAgain() =>
        CommandGenerators.Scripts.SampleAsync(async script =>
        {
            var world = await LedgerWorld.StartAsync();
            foreach (Command command in script.Commands)
            {
                var send = world.Prepare(command);
                await send();
                string afterFirst = await world.SnapshotAsync();
                if (command is Advance) continue;

                await send();
                Assert.Equal(afterFirst, await world.SnapshotAsync());
            }
        });

    [Fact]
    public Task CreditsPixAtMostOncePerMemberAndCompetence() =>
        CommandGenerators.Scripts.SampleAsync(async script =>
        {
            var world = await LedgerWorld.RunAsync(script);
            var pix = world.Posted.Where(transaction => transaction.Header.ExternalReference is not null).ToList();

            long credited = pix.Sum(transaction => transaction.Entries.Where(entry => entry.SignedCents > 0).Sum(entry => entry.SignedCents));
            Assert.Equal(pix.Count, pix.Select(transaction => transaction.Header.Key).Distinct().Count());
            Assert.True(credited <= Membership.MonthlyAllowance.Cents * world.CreditedPeriodCount, $"credited {credited} for {world.CreditedPeriodCount} paid competences");
        });

    [Fact]
    public void RoundsAPercentageToTheNearestCentavoWithinTheValue() =>
        Gen.Select(Gen.Long[0, MaxCents], Gen.Int[0, 1_000_000], (cents, millionths) => (Cents: cents, Rate: millionths / 1_000_000m))
            .Sample(input =>
            {
                long result = Money.FromCents(input.Cents).PercentOf(input.Rate).Cents;

                Assert.InRange(result, 0, input.Cents);
                Assert.True(Math.Abs(result - (input.Cents * input.Rate)) <= 0.5m, $"{input.Cents} x {input.Rate} gave {result}");
            });

    [Fact]
    public void ExpiresNeitherMoreThanWasCreditedNorMoreThanTheUnspentPositiveBalance() =>
        Gen.Select(Gen.Long[0, MaxCents], Gen.Long[0, MaxCents], (credited, debited) => (Credited: credited, Debited: debited))
            .Sample(input =>
            {
                long expiring = CreditExpiry.Expiring(new ExpiryTotals(Money.FromCents(input.Credited), Money.FromCents(input.Debited))).Cents;

                Assert.True(expiring >= 0);
                Assert.True(expiring <= input.Credited);
                Assert.True(expiring <= Math.Max(0, input.Credited - input.Debited));
            });

    // Oldest first: a redemption or an expiry may never be what pushes the running balance below zero, and any debt that
    // exists at the end must be explained by a reversal that took a credit back.
    private static void AssertDebtComesOnlyFromReversals(IReadOnlyList<StatementLine> newestFirst)
    {
        long running = 0;
        foreach (StatementLine line in newestFirst.Reverse())
        {
            running += line.AmountCents;
            if (IsSpend(line)) Assert.True(running >= 0, $"'{line.Description}' left the balance at {running}");
        }

        if (running < 0) Assert.Contains(newestFirst, line => line.Description.StartsWith("Reversal of", StringComparison.Ordinal) && line.AmountCents < 0);
    }

    private static bool IsSpend(StatementLine line) =>
        line.AmountCents < 0 && (line.Description.StartsWith("Redemption ", StringComparison.Ordinal) || line.Description.StartsWith("Credit expired", StringComparison.Ordinal));
}
