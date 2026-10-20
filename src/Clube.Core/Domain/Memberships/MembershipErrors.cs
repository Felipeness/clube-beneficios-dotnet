using Clube.Core.Domain.Shared;

namespace Clube.Core.Domain.Memberships;

public static class MembershipErrors
{
    public static readonly Error InvalidCpf =
        new("membership.invalid_cpf", "CPF is invalid.", ErrorKind.Validation);

    public static readonly Error InvalidPeriod =
        new("membership.invalid_period", "Billing period must be formatted as yyyy-MM.", ErrorKind.Validation);

    public static readonly Error NotFound =
        new("membership.not_found", "Membership not found.", ErrorKind.NotFound);

    public static readonly Error RewardNotFound =
        new("membership.reward_not_found", "Reward not found.", ErrorKind.NotFound);

    public static readonly Error AlreadySubscribed =
        new("membership.already_subscribed", "This CPF already holds a membership.", ErrorKind.Conflict);

    public static readonly Error ConcurrentUpdate =
        new("membership.concurrent_update", "Membership changed during the request, retry.", ErrorKind.Conflict);

    public static readonly Error PeriodOutOfOrder =
        new("membership.period_out_of_order", "Billing period precedes the last credited one.", ErrorKind.BusinessRule);

    public static readonly Error InsufficientCredits =
        new("membership.insufficient_credits", "Not enough credits for this reward.", ErrorKind.BusinessRule);
}
