namespace EggIncognito.Services.Predictions;

public class DataVersion {
    private long _version;
    public long Version => Interlocked.Read(ref _version);
    public void Bump() => Interlocked.Increment(ref _version);
}
