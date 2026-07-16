namespace EggIncognito.Services.Backfill;


public static class SourcePrecedence
{
    private static int Rank(string s) => s switch { "farm" => 3, "elgranjero" => 2, _ => 1 };

   
    public static bool MayOverwriteProto(string existingSource, string incomingSource) =>
        incomingSource != "playstore" && incomingSource != "appstore"
        && Rank(incomingSource) >= Rank(existingSource);
}
