namespace EggIncognito.Models.AdminUi;

public record SessionRow(string Key, string Kind, bool Killable, bool Running, int Port, long Flows, long Connections, int Devices, long DecryptOk, int DecryptErr);
