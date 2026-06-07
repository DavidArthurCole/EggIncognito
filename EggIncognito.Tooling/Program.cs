using EggIncognito.Tooling.Capture;

// EggIncognito.Tooling - the C# replacement for the .ps1 + Python capture toolchain.
// Subcommand dispatch. `capture` is the first command; room left for extract/check/sync-proto.

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];
var rest = args[1..];

return command switch
{
    "capture" => await CaptureCommand.RunAsync(rest),
    "emit-types" => EggIncognito.Tooling.TypeEmitter.Run(FindRepoRoot()),
    "-h" or "--help" or "help" => Usage(),
    _ => Unknown(command),
};

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
            return dir.FullName;
        dir = dir.Parent;
    }
    return Directory.GetCurrentDirectory();
}

int Usage() { PrintUsage(); return 0; }
int Unknown(string c) { Console.Error.WriteLine($"Unknown command: {c}"); PrintUsage(); return 1; }

void PrintUsage()
{
    Console.WriteLine("""
        EggIncognito.Tooling - capture and fixture tooling

        Usage:
          capture [options]
              Run a selective-decrypt proxy. Decrypts only auxbrain hosts; passes everything
              else through. Captured flows are written to captures/*.har, fed into the fixture
              pipeline in-process (fixtures + routes.yaml self-repair), and streamed to a
              live web dashboard.

              Options:
                --port <n>             proxy listen port (default 8080)
                --dashboard-port <n>   dashboard web port (default 8090)
                --eid EI...            your EID (also read from EGG_INC_EID); scrubbed from output
                --label <name>         label added to the HAR filename
                --overwrite            overwrite existing fixtures instead of staging diffs
                --verbose, -v          trace every CONNECT / request / response (diagnostics)
                --no-dashboard         console-only; do not start the web dashboard
                --no-open              start the dashboard but do not open a browser

          emit-types
              Regenerate wwwroot/capture/types.d.ts from the C# dashboard records so the SPA's
              JSDoc type checks stay in sync with the wire shapes. Run after changing a record.
        """);
}
