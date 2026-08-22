namespace EggIncognito.Models.AdminUi;

public record ConsoleEndpoint(string Method, string Route, List<ConsoleParamRow> Query, bool HasBody, string? BodyType, bool HasFile);
