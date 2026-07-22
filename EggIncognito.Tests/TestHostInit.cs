using System.Runtime.CompilerServices;

namespace EggIncognito.Tests;


internal static class TestHostInit {
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("EGGINCOGNITO_TEST_DBFREE", "1");
}
