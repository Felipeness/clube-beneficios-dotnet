using Clube.Core.Domain.Memberships;

namespace Clube.Core.Tests;

public class HexagonBoundaryTests
{
    // The dependency rule lives in the csproj today; this fails the build the day someone adds a reference.
    [Theory]
    [InlineData("Clube.Adapters")]
    [InlineData("Microsoft.AspNetCore")]
    [InlineData("Microsoft.EntityFrameworkCore")]
    public void CoreDoesNotReferenceTheOutside(string forbiddenPrefix)
    {
        var references = typeof(Membership).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name?.StartsWith(forbiddenPrefix, StringComparison.Ordinal) == true);
    }
}
