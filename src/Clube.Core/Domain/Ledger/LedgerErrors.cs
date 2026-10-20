using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Ledger;

public static class LedgerErrors
{
    public static readonly Error InvalidIdempotencyKey =
        new("ledger.invalid_idempotency_key", "Idempotency-Key must have 1 to 64 characters.", ErrorKind.Validation);

    public static readonly Error InvalidPaymentReference =
        new("ledger.invalid_payment_reference", "Payment reference must have 1 to 64 letters or digits.", ErrorKind.Validation);

    public static readonly Error IdempotencyKeyReused =
        new("ledger.idempotency_key_reused", "This Idempotency-Key was already used for a different request.", ErrorKind.BusinessRule);

    public static readonly Error TooFewEntries =
        new("ledger.too_few_entries", "A transaction needs at least two entries.", ErrorKind.BusinessRule);

    public static readonly Error NonPositiveAmount =
        new("ledger.non_positive_amount", "Every entry must move a positive amount.", ErrorKind.BusinessRule);

    public static readonly Error ReversalOfReversal =
        new("ledger.reversal_of_reversal", "A reversal cannot be reversed.", ErrorKind.BusinessRule);

    public static readonly Error Unbalanced =
        new("ledger.unbalanced", "Debits and credits must sum to zero.", ErrorKind.BusinessRule);
}
