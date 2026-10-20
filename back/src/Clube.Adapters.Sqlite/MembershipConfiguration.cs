using Clube.Core.Domain.Memberships;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Clube.Adapters.Sqlite;

internal sealed class MembershipConfiguration(PiiProtector pii) : IEntityTypeConfiguration<Membership>
{
    // Shadow property: the blind index is a persistence concern, the domain never sees it.
    public const string HolderIndex = nameof(HolderIndex);

    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.ToTable("memberships");
        builder.HasKey(membership => membership.Id);

        builder.Property(membership => membership.Id)
            .HasConversion(id => id.Value, value => new MembershipId(value));

        // Encrypted at rest; the domain keeps the CPF in the clear and never learns about it.
        builder.Property(membership => membership.Holder)
            .HasConversion(cpf => pii.Encrypt(cpf.Digits), stored => RehydrateCpf(pii.Decrypt(stored)));

        // The ciphertext is randomised, so "one member per CPF" lives on the deterministic HMAC instead.
        builder.Property<string>(HolderIndex).IsRequired().HasMaxLength(64);
        builder.HasIndex(HolderIndex).IsUnique();

        // Every posting bumps the version; a stale writer fails here instead of overspending the balance.
        builder.Property(membership => membership.LedgerVersion).IsConcurrencyToken();
    }

    private static Cpf RehydrateCpf(string digits) =>
        Cpf.Parse(digits).TryGetValue(out var cpf, out var error)
            ? cpf
            : throw new InvalidOperationException($"Stored holder failed validation: {error.Code}");
}
