namespace EggIncognito.Services.Devices;

public sealed record IntegrityFile(string RelativePath, byte[] Bytes, bool Exec);
