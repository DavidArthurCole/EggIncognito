namespace EggIncognito.Models.ApiKeys;

public record KeysResponse(List<ApiKeysPanelRow> Keys, int? Cap);
