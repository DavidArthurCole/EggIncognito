namespace EggIncognito.Services;

public static class IdentityConfigKeys {
    public const string ApiUrl = "Identity:ApiUrl";
    public const string ApiSecret = "Identity:ApiSecret";
    public const string WidgetUrl = "Identity:WidgetUrl";
}

public static class DecompConfigKeys {
    public const string BinaryPath = "Decomp:BinaryPath";
    public const string SymbolizedIpaDir = "Decomp:SymbolizedIpaDir";
    public const string LiveDevicePull = "Decomp:LiveDevicePull";
    public const string LiveCacheSeconds = "Decomp:LiveCacheSeconds";
    public const string StrippedTargetPath = "Decomp:StrippedTargetPath";
}
