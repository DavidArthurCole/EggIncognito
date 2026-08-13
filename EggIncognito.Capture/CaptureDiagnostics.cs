namespace EggIncognito.Capture;

public static class CaptureDiagnostics {
    public static Action<string, string, Exception> Failed { get; set; } =
        (operation, context, ex) => Console.Error.WriteLine($"capture {operation} failed ({context}): {ex}");
}
