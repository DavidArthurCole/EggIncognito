namespace EggIncognito.Runner.Extract;

// Reads the proto/API clientVersion (e.g. 72) from a pulled arm split, or null when not determinable.
// Null is a valid, registry-accepted value. The real reader lands only after a recipe is proven on the
// device (see the plan's clientVersion verification task); until then NullClientVersionReader is wired.
public interface IClientVersionReader
{
    string? Read(string apkPath);
}
