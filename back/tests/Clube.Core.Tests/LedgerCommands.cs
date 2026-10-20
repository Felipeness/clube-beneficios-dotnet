using Clube.Core.Application;
using Clube.Core.Domain.Ledger;
using Clube.Core.Domain.Memberships;
using Clube.Core.Ports.Driving;
using CsCheck;
using Microsoft.Extensions.Time.Testing;

namespace Clube.Core.Tests;

// Everything a client of the hexagon can do to the ledger, as data, so a generator can build sequences of it and a failing
// sequence shrinks to a readable list. Members, references and keys are indexes into small pools on purpose: a collision
// (the same Pix id twice, the same idempotency key again) is the interesting case, and a random Guid would never produce it.
internal abstract record Command;

internal sealed record Pay(int Member, int MonthOffset, int Reference, long AmountCents) : Command;

internal sealed record Redeem(int Member, string Reward, int Key) : Command;

internal sealed record Reverse(int Member, int Pick) : Command;

internal sealed record Refund(int Reference) : Command;

internal sealed record RunExpiry : Command;

internal sealed record Advance(int Days) : Command;

internal sealed record CommandScript(IReadOnlyList<Command> Commands)
{
    public override string ToString() =>
        Environment.NewLine + string.Join(Environment.NewLine, Commands.Select((command, index) => $"{index + 1}. {command}"));
}

internal static class CommandGenerators
{
    private const int MemberPool = 2;
    private const int ReferencePool = 6;
    private const int KeyPool = 3;
    private const int MaxScriptLength = 40;

    // Mostly the exact allowance, sometimes a wrong amount; offsets run from before the subscription month to the future.
    private static readonly Gen<Command> PayGen = Gen.Select(
        Gen.Int[0, MemberPool - 1],
        Gen.Int[-1, 18],
        Gen.Int[0, ReferencePool - 1],
        Gen.OneOfConst(2000L, 2000L, 2000L, 1500L),
        (member, offset, reference, amount) => (Command)new Pay(member, offset, reference, amount));

    private static readonly Gen<Command> RedeemGen = Gen.Select(
        Gen.Int[0, MemberPool - 1],
        Gen.OneOfConst("cinema-ticket", "perfume-kit", "thermal-bottle", "no-such-reward"),
        Gen.Int[0, KeyPool - 1],
        (member, reward, key) => (Command)new Redeem(member, reward, key));

    private static readonly Gen<Command> ReverseGen = Gen.Select(
        Gen.Int[0, MemberPool - 1],
        Gen.Int[0, 20],
        (member, pick) => (Command)new Reverse(member, pick));

    private static readonly Gen<Command> RefundGen = Gen.Int[0, ReferencePool - 1].Select(reference => (Command)new Refund(reference));

    private static readonly Gen<Command> ExpiryGen = Gen.Const<Command>(new RunExpiry());

    // Days to months and past the 13 months an expiry cutoff needs, so the payment window and the expiry both get exercised.
    private static readonly Gen<Command> AdvanceGen = Gen.OneOfConst(1, 20, 31, 95, 200, 400).Select(days => (Command)new Advance(days));

    private static readonly Gen<Command> AnyCommand = Gen.OneOf(PayGen, PayGen, RedeemGen, RedeemGen, ReverseGen, RefundGen, ExpiryGen, AdvanceGen, AdvanceGen);

    public static Gen<CommandScript> Scripts { get; } = AnyCommand.List[1, MaxScriptLength].Select(commands => new CommandScript(commands));
}

// The hexagon wired to the in-memory store and a fake clock, driven only through its driving ports, plus what the test
// needs to remember to judge it afterwards.
internal sealed class LedgerWorld
{
    private static readonly string[] Cpfs = ["529.982.247-25", "111.444.777-35"];
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-20T15:00:00Z");

    private readonly InMemoryMembershipStore store = new();
    private readonly FakeTimeProvider clock = new(Start);
    private readonly HashSet<(int Member, BillingPeriod Period)> creditedPeriods = [];
    private readonly List<MembershipId> members = [];
    private readonly IForManagingMemberships hexagon;
    private readonly CreditExpiryRunner expiry;

    private LedgerWorld()
    {
        hexagon = new MembershipManager(store, new FakeCaller(Actor.Operator("operator-1"), "corr-1"), clock);
        expiry = new CreditExpiryRunner(store, clock);
    }

    public static async Task<LedgerWorld> StartAsync()
    {
        var world = new LedgerWorld();
        foreach (string cpf in Cpfs) world.members.Add(new MembershipId(ResultAssert.Ok(await world.hexagon.SubscribeAsync(cpf, default)).Id));
        return world;
    }

    public static async Task<LedgerWorld> RunAsync(CommandScript script)
    {
        var world = await StartAsync();
        foreach (Command command in script.Commands) await world.Prepare(command)();
        return world;
    }

    public int MemberCount => members.Count;

    public IReadOnlyList<LedgerTransaction> Posted => store.PostedTransactions;

    // Payments the hexagon said it credited, one per member and period: the ceiling the Pix total must respect.
    public int CreditedPeriodCount => creditedPeriods.Count;

    // Fixes everything the command depends on (which transaction a reversal targets) at preparation, so running the
    // returned action twice is a true retry of the same request, not a second, different one.
    public Func<Task> Prepare(Command command) => command switch
    {
        Pay pay => () => PayAsync(pay),
        Redeem redeem => () => hexagon.RedeemRewardAsync(members[redeem.Member], redeem.Reward, $"key-{redeem.Key}", default),
        Reverse reverse => ReversalOf(reverse),
        Refund refund => () => hexagon.RefundPaymentAsync(ReferenceOf(refund.Reference), default),
        RunExpiry => () => expiry.RunAsync(CreditExpiry.CutoffAt(clock.GetUtcNow()), default),
        Advance advance => () => AdvanceAsync(advance.Days),
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "unknown command"),
    };

    public async Task<long> BalanceAsync(int member) =>
        ResultAssert.Ok(await hexagon.GetAsync(members[member], default)).BalanceCents;

    // Newest first, followed to the last page like a client would.
    public async Task<IReadOnlyList<StatementLine>> StatementAsync(int member)
    {
        var lines = new List<StatementLine>();
        StatementCursor? after = null;
        do
        {
            var page = ResultAssert.Ok(StatementPage.Create(StatementPage.MaxLimit, after));
            var statement = ResultAssert.Ok(await hexagon.GetStatementAsync(members[member], page, default));
            lines.AddRange(statement.Lines);
            after = statement.Next;
        }
        while (after is not null);

        return lines;
    }

    public async Task<string> SnapshotAsync()
    {
        var balances = new List<long>();
        for (int member = 0; member < MemberCount; member++) balances.Add(await BalanceAsync(member));
        return $"postings={Posted.Count} balances=[{string.Join(", ", balances)}]";
    }

    private async Task PayAsync(Pay pay)
    {
        var period = BillingPeriod.At(Start).AddMonths(pay.MonthOffset);
        var payment = new ReceivedPayment(ReferenceOf(pay.Reference), period.ToString(), pay.AmountCents);
        var outcome = await hexagon.CreditMonthlyAllowanceAsync(members[pay.Member], payment, default);
        if (outcome.TryGetValue(out var credited, out _) && credited == AllowanceCreditOutcome.Credited) creditedPeriods.Add((pay.Member, period));
    }

    private Func<Task> ReversalOf(Reverse reverse)
    {
        var member = members[reverse.Member];
        var account = LedgerAccount.Member(member);
        var touching = store.PostedTransactions.Where(transaction => transaction.Entries.Any(entry => entry.Account == account)).ToList();
        Guid target = touching.Count == 0 ? Guid.Empty : touching[reverse.Pick % touching.Count].Id;
        return () => hexagon.ReverseTransactionAsync(member, target, default);
    }

    private Task AdvanceAsync(int days)
    {
        clock.Advance(TimeSpan.FromDays(days));
        return Task.CompletedTask;
    }

    private static string ReferenceOf(int index) => $"E{index}";
}
