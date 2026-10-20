using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Clube.Adapters.Sqlite;

// Only the dotnet ef tooling uses this, to build a context without booting the host. The keys are obvious placeholders: the
// migration SQL never contains key material (the converter only runs when values are written), and nothing here reaches runtime.
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ClubeDbContext>
{
    private static readonly byte[] EncryptionKey = Encoding.UTF8.GetBytes("design-time-only-encryption-key!");
    private static readonly byte[] IndexKey = Encoding.UTF8.GetBytes("design-time-only-blind-index-ky!");

    public ClubeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ClubeDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .ReplaceService<Microsoft.EntityFrameworkCore.Infrastructure.IModelCacheKeyFactory, PiiModelCacheKeyFactory>()
            .Options;
        return new ClubeDbContext(options, new PiiProtector(EncryptionKey, IndexKey));
    }
}
