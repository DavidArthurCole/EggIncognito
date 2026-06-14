namespace EggIncognito.Services.Backfill;

// Decides what a backfill row may overwrite given the existing row's source. Farm (device-extracted)
// is authoritative for proto text; elgranjero fills proto + clientVersion when farm is absent; store
// sources never set proto text.
public static class SourcePrecedence
{
    private static int Rank(string s) => s switch { "farm" => 3, "elgranjero" => 2, _ => 1 };

    // May the incoming source overwrite the existing row's PROTO text?
    public static bool MayOverwriteProto(string existingSource, string incomingSource) =>
        incomingSource != "playstore" && incomingSource != "appstore"
        && Rank(incomingSource) >= Rank(existingSource);
}
