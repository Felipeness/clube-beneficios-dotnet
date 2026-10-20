using Clube.Core.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clube.Adapters.Sqlite;

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

        // Every posting bumps the version; a stale writer fails here instead of overspending the balance.
        builder.Property(membership => membership.LedgerVersion).IsConcurrencyToken();
    }

    private static Cpf RehydrateCpf(string digits) =>
        Cpf.Parse(digits).TryGetValue(out var cpf, out var error)
            ? cpf
            : throw new InvalidOperationException($"Stored holder failed validation: {error.Code}");
}
