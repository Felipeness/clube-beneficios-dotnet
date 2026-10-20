using Clube.Domain.Memberships;

namespace Clube.Domain.Tests;

public class CpfTests
{
    [Theory]
    [InlineData("529.982.247-25")]
    [InlineData("52998224725")]
    [InlineData("111.444.777-35")]
    public void AcceptsValidCpf(string raw) => ResultAssert.Ok(Cpf.Parse(raw));

    [Theory]
    [InlineData("529.982.247-24")]
    [InlineData("111.111.111-11")]
    [InlineData("5299822472")]
    [InlineData("529.982.247-2a")]
    [InlineData("")]
    public void RejectsInvalidCpf(string raw) =>
        Assert.Equal(MembershipErrors.InvalidCpf, ResultAssert.Failed(Cpf.Parse(raw)));

    [Fact]
    public void MasksEdgesWhenFormatted()
    {
        var cpf = ResultAssert.Ok(Cpf.Parse("529.982.247-25"));

        Assert.Equal("***.982.247-**", $"{cpf}");
    }
}
