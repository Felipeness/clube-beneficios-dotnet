using Clube.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Clube.Infrastructure.Persistence;

internal sealed class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .HasConversion(id => id.Value, value => new MembershipId(value));

        builder.Property(membership => membership.Holder)
            .HasConversion(cpf => cpf.Digits, digits => RehydrateCpf(digits))
            .HasMaxLength(11);
        builder.HasIndex(membership => membership.Holder).IsUnique();

        // Two concurrent redemptions read the same balance; the token turns the second
        // write into a conflict instead of a silent lost update.
        builder.Property(membership => membership.Balance)
            .HasConversion(credits => credits.Value, value => Credits.Of(value))
            .IsConcurrencyToken();

        builder.Property(membership => membership.LastCreditedPeriod)
            .HasConversion(new ValueConverter<BillingPeriod, DateOnly>(
                period => period.Start,
                start => BillingPeriod.Containing(start)));
    }

    private static Cpf RehydrateCpf(string digits) =>
        Cpf.Parse(digits).TryGetValue(out var cpf, out var error)
            ? cpf
            : throw new InvalidOperationException($"Stored holder failed validation: {error.Code}");
}
