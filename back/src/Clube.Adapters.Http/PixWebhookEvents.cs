using System.ComponentModel;
using System.Text.Json.Serialization;

namespace Clube.Adapters.Http;

// The provider's wire format. The "type" discriminator picks the event; the payment provider's own
// field names are kept so the contract reads the same on both sides.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(PixReceived), PixReceived.Type)]
[JsonDerivedType(typeof(PixRefunded), PixRefunded.Type)]
public abstract record PixWebhookEvent;

public sealed record PixReceived(
    [property: Description("Pix endToEndId of the payment. Redelivering it is a no-op.")] string EndToEndId,
    Guid MembershipId,
    [property: Description("Billing period paid, as yyyy-MM.")] string Period,
    [property: Description("Must equal the monthly allowance (2000), otherwise 422.")] long AmountCents,
    DateTimeOffset PaidAt) : PixWebhookEvent
{
    public const string Type = "pix.received";
}

public sealed record PixRefunded(
    [property: Description("Pix endToEndId of the refund itself (devolução, including MED).")] string EndToEndId,
    [property: Description("endToEndId of the payment being refunded.")] string OriginalEndToEndId,
    DateTimeOffset RefundedAt) : PixWebhookEvent
{
    public const string Type = "pix.refunded";
}

public sealed record PixWebhookResponse(
    [property: Description("credited, already_credited, duplicate_payment (a second payment for a paid period: support refunds it), refunded, already_refunded or unknown_payment.")]
    string Result);
