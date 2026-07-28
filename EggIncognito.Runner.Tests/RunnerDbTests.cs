using EggIncognito.Runner.Data;
using Xunit;

namespace EggIncognito.Runner.Tests;

public class RunnerDbTests {
    [Fact]
    public void FromEnv_NullWhenNoConnString() {
        var db = RunnerDb.FromEnv(_ => "");
        Assert.Null(db);
    }

    [Fact]
    public void FromEnv_ConfiguredWhenConnStringPresent() {
        var db = RunnerDb.FromEnv(k => k == "ConnectionStrings__Postgres" ? "Host=localhost;Database=x" : "");
        Assert.NotNull(db);
        Assert.Equal("Host=localhost;Database=x", db!.ConnectionString);
    }

    [Fact]
    public void NewContext_BuildsAContext() {
        var db = RunnerDb.FromEnv(k => k == "ConnectionStrings__Postgres" ? "Host=localhost;Database=x" : "")!;
        using var ctx = db.NewContext();
        Assert.NotNull(ctx);
    }
}
