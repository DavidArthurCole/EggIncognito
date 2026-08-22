namespace EggIncognito.Models.Playground;

public record RollbackResult(string RolledBack, int FromVersion, int NewVersion);
