namespace EggIncognito.Startup;

public static class CaptureLaunchArgs {
    public static bool Apply(string[] args) {
        if (!args.Contains("--capture")) return false;
        if (Value(args, "--eid") is { } eid) Environment.SetEnvironmentVariable("EGG_INC_EID", eid);
        if (Value(args, "--label") is { } label) Environment.SetEnvironmentVariable("CaptureLabel", label);
        if (args.Contains("--overwrite")) Environment.SetEnvironmentVariable("CaptureOverwrite", "true");
        return true;
    }

    private static string? Value(string[] args, string name) {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
