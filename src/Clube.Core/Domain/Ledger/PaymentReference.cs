using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Ledger;

// The payment provider's own id for a payment (a Pix endToEndId): what ties a posting back to money that really moved,
// and what a refund later points at.
public readonly record struct PaymentReference
{
    private const int MaxLength = 64;

    public string Value { get; }

    private PaymentReference(string value) => Value = value;

    public static Result<PaymentReference> Parse(string raw) =>
        raw.Length is > 0 and <= MaxLength && raw.All(char.IsAsciiLetterOrDigit)
            ? new PaymentReference(raw)
            : LedgerErrors.InvalidPaymentReference;

    public override string ToString() => Value;
}
