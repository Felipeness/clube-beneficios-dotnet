using Clube.Core.Domain.Memberships;
using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Ledger;

// Position of a ledger entry in insertion order. The core only knows it as "everything older than this";
// how a client carries it (base64, JSON, whatever) is an edge concern.
public sealed record StatementCursor(long Position);

public sealed class StatementPage
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    public static readonly StatementPage Default = new(DefaultLimit, null);

    private StatementPage(int limit, StatementCursor? after)
    {
        Limit = limit;
        After = after;
    }

    public int Limit { get; }

    /// <summary>Resume strictly after this entry (older ones only); null starts at the newest.</summary>
    public StatementCursor? After { get; }

    // Rejects instead of clamping: a client asking for 500 would otherwise believe it got 500 and stop early.
    public static Result<StatementPage> Create(int? limit, StatementCursor? after)
    {
        int requested = limit ?? DefaultLimit;
        if (requested is < 1 or > MaxLimit) return MembershipErrors.InvalidLimit;

        return new StatementPage(requested, after);
    }
}

public sealed record PositionedLine(long Position, StatementLine Line);

public sealed record StatementSlice(IReadOnlyList<StatementLine> Lines, StatementCursor? Next)
{
    // Adapters fetch one row beyond the limit: its mere existence says another page follows, no COUNT needed.
    public static StatementSlice From(IReadOnlyList<PositionedLine> fetchedPlusOne, StatementPage page)
    {
        var pageRows = fetchedPlusOne.Take(page.Limit).ToList();
        bool hasMore = fetchedPlusOne.Count > page.Limit;
        var next = hasMore ? new StatementCursor(pageRows[^1].Position) : null;
        return new StatementSlice([.. pageRows.Select(row => row.Line)], next);
    }
}
