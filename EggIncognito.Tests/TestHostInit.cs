using System.Runtime.CompilerServices;

namespace EggIncognito.Tests;

// Runs once when the test assembly loads, before any test or WebApplicationFactory<Program> boots.
// Sets the marker Program.cs reads to clear ConnectionStrings:Postgres, so every integration test
// runs DB-free by default. The dev box carries a Host=frame Postgres connection string in user-secrets;
// without this guard WebApplicationFactory<Program> would boot DB-enabled and integration tests would
// hit (and mutate) the live shared DB. A test needing a DB opts in via the TestDbOptIn setting.
internal static class TestHostInit
{
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE", "1");
}
