using System.Runtime.CompilerServices;

namespace EggIncognito.Tests;

// Runs once when the test assembly loads, before any WebApplicationFactory<Program> boots. Sets the
// marker Program.cs reads to clear ConnectionStrings:Postgres, so every integration test runs DB-free
// by default; a test needing a DB opts in via the TestDbOptIn setting.
internal static class TestHostInit
{
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE", "1");
}
