using EggIncognito.Data.Services;
using Microsoft.EntityFrameworkCore;

namespace EggIncognito.Runner.Data;

public sealed class RunnerDb {
    public string ConnectionString { get; }

    private RunnerDb(string connectionString) {
        ConnectionString = connectionString;
    }

    public static RunnerDb? FromEnv(Func<string, string> env) {
        var conn = env("ConnectionStrings__Postgres");
        return string.IsNullOrWhiteSpace(conn) ? null : new RunnerDb(conn);
    }

    public EggIncognitoDbContext NewContext() {
        var options = new DbContextOptionsBuilder<EggIncognitoDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new EggIncognitoDbContext(options);
    }
}
