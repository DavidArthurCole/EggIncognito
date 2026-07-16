namespace EggIncognito.Runner.Extract;
public interface IClientVersionReader
{
    string? Read(string apkPath, int? previousClientVersion);
}
