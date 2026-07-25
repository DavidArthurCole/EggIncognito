namespace EggIncognito.Core.Services.Devices;

public static class DeviceCaptureVerdict {
    public static Verdict For(in Counters c) {
        if (!c.Listening) {
            return new Verdict("listener down", "text-red-400",
                "no per-device capture listener bound (DeviceCapture disabled or failed to start)");
        }

        if (c.RinfoHarvests > 0)
            return new Verdict("ok", "text-green-400", $"{c.RinfoHarvests} rinfo harvested, {c.Flows} flows");
        if (c.Flows > 0) {
            return new Verdict("no rinfo in flows", "text-yellow-400",
                $"{c.Flows} flows decoded but none carried rinfo (request types lack BasicRequestInfo, or field mismatch)");
        }

        return c.AuxbrainConnects > 0
            ? new Verdict("CA untrusted", "text-red-400",
                c.LastDecryptError ??
                $"{c.AuxbrainConnects} auxbrain CONNECTs but 0 decrypted flows: the capture CA is not trusted on the device")
            : c.ClientConnects > 0
                ? new Verdict("not reaching auxbrain", "text-yellow-400",
                    $"{c.ClientConnects} client connects but 0 auxbrain CONNECTs: traffic reaches the proxy but not auxbrain (DNS / host filter)")
                : new Verdict("not routing", "text-red-400",
                    $"listening on :{c.Port} but 0 client connects: the device is not routing through the proxy (proxy not applied, or the app bypasses the system proxy)");
    }

    public readonly record struct Counters(
        bool Listening,
        int Port,
        long ClientConnects,
        long AuxbrainConnects,
        long Flows,
        long RinfoHarvests,
        string? LastDecryptError);

    public readonly record struct Verdict(string Label, string Color, string Detail);
}
