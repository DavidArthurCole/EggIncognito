using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EggIncognito.Data.Services;

// Lets `dotnet ef migrations` build the context at design time without a running app or live DB.
// The connection string here is only used to construct the model, never to connect during `add`.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<EggIncognitoDbContext>
{
    public EggIncognitoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql("Host=localhost;Database=eggincognito_designtime")
            .Options;
        return new EggIncognitoDbContext(options);
    }
}
