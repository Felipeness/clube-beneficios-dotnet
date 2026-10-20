using System.Security.Cryptography;
using Clube.Adapters.Sqlite;

namespace Clube.EndToEnd.Tests;

// Unit tests of the adapter's cipher; they live here because this is the test project that already sees the adapter.
public class PiiProtectorTests
{
    private const string Digits = "52998224725";

    private static PiiProtector Protector(byte encryptionSeed = 1, byte indexSeed = 2) =>
        new(Key(encryptionSeed), Key(indexSeed));

    private static byte[] Key(byte seed) => [.. Enumerable.Repeat(seed, PiiOptions.KeyBytes)];

    [Fact]
    public void RoundTripsThePlaintext() =>
        Assert.Equal(Digits, Protector().Decrypt(Protector().Encrypt(Digits)));

    [Fact]
    public void EncryptsTheSameValueDifferentlyEachTime()
    {
        var protector = Protector();

        Assert.NotEqual(protector.Encrypt(Digits), protector.Encrypt(Digits));
    }

    [Fact]
    public void RefusesToDecryptWithAnotherKey()
    {
        string stored = Protector(encryptionSeed: 1).Encrypt(Digits);

        Assert.ThrowsAny<CryptographicException>(() => Protector(encryptionSeed: 9).Decrypt(stored));
    }

    [Theory]
    [InlineData("v2:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("52998224725")]
    [InlineData("v1:AAAA")]
    public void RefusesUnknownVersionsAndShortPayloads(string stored) =>
        Assert.ThrowsAny<CryptographicException>(() => Protector().Decrypt(stored));

    [Fact]
    public void DerivesADeterministicIndexThatDependsOnTheIndexKey()
    {
        string index = Protector(indexSeed: 2).BlindIndex(Digits);

        Assert.Equal(index, Protector(indexSeed: 2).BlindIndex(Digits));
        Assert.NotEqual(index, Protector(indexSeed: 3).BlindIndex(Digits));
        Assert.DoesNotContain(Digits, index);
    }
}
