namespace Clube.Core.Ports.Driving;

// Every outcome here is a success for the payment provider: it stops redelivering, and the duplicate or
// unknown cases are surfaced to support instead of being retried forever.
public enum AllowanceCreditOutcome
{
    Credited,
    AlreadyCredited,
    DuplicatePayment,
}

public enum PaymentRefundOutcome
{
    Refunded,
    AlreadyRefunded,
    UnknownPayment,
}

/// <summary>A payment as the provider reported it, before the hexagon validates any of it.</summary>
public sealed record ReceivedPayment(string Reference, string Period, long AmountCents);
