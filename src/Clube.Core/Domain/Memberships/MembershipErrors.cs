using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public static class MembershipErrors
{
    public static readonly Error InvalidCpf =
        new("membership.invalid_cpf", "CPF is invalid.", ErrorKind.Validation);

    public static readonly Error InvalidPeriod =
        new("membership.invalid_period", "Billing period must be formatted as yyyy-MM.", ErrorKind.Validation);

    public static readonly Error InvalidLimit =
        new("membership.invalid_limit", "Statement limit must be between 1 and 100.", ErrorKind.Validation);

    public static readonly Error InvalidCursor =
        new("membership.invalid_cursor", "Statement cursor is invalid.", ErrorKind.Validation);

    public static readonly Error NotFound =
        new("membership.not_found", "Membership not found.", ErrorKind.NotFound);

    public static readonly Error TransactionNotFound =
        new("membership.transaction_not_found", "Transaction not found for this membership.", ErrorKind.NotFound);

    public static readonly Error RewardNotFound =
        new("membership.reward_not_found", "Reward not found.", ErrorKind.NotFound);

    public static readonly Error AlreadySubscribed =
        new("membership.already_subscribed", "This CPF already holds a membership.", ErrorKind.Conflict);

    public static readonly Error ConcurrentUpdate =
        new("membership.concurrent_update", "Membership changed during the request, retry.", ErrorKind.Conflict);

    public static readonly Error FuturePeriod =
        new("membership.future_period", "Billing period is in the future.", ErrorKind.BusinessRule);

    public static readonly Error PeriodBeforeSubscription =
        new("membership.period_before_subscription", "Billing period is before the membership started.", ErrorKind.BusinessRule);

    public static readonly Error InsufficientBalance =
        new("membership.insufficient_balance", "Balance does not cover this reward.", ErrorKind.BusinessRule);
}
