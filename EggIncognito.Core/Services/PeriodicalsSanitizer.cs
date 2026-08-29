using Ei;
using Google.Protobuf;

namespace EggIncognito.Core.Services;

public static class PeriodicalsSanitizer {
    public static string ScrubPlayerScope(string? responseType, string json) {
        try {
            if (string.Equals(responseType, PeriodicalsResponse.Descriptor.Name, StringComparison.Ordinal)) {
                var msg = JsonParser.Default.Parse<PeriodicalsResponse>(json);
                ScrubPlayerScope(msg);
                return ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg));
            }

            if (string.Equals(responseType, ContractsResponse.Descriptor.Name, StringComparison.Ordinal)) {
                var msg = JsonParser.Default.Parse<ContractsResponse>(json);
                ScrubPlayerScope(msg);
                return ProtoJson.PrettyPrint(JsonFormatter.Default.Format(msg));
            }

            return json;
        } catch (InvalidProtocolBufferException) {
            return json;
        } catch (InvalidJsonException) {
            return json;
        }
    }

    public static void ScrubPlayerScope(PeriodicalsResponse msg) {
        msg.Gifts.Clear();
        msg.Evaluations.Clear();
        msg.ArtifactCases.Clear();
        msg.ShowcaseRoyalties.Clear();
        msg.ContractPlayerInfo = null;
        if (msg.Contracts is not null) ScrubPlayerScope(msg.Contracts);
    }

    public static void ScrubPlayerScope(ContractsResponse msg) => msg.ClearTotalEop();
}
