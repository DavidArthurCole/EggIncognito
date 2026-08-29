namespace EggIncognito.Capture;

public static class LimitedFlowProjector {
    public static DashboardFlow Project(DashboardFlow flow, IReadOnlySet<string> fullDetailRoutes) =>
        fullDetailRoutes.Contains(flow.Path) ? flow : Strip(flow);

    public static DashboardFlow Strip(DashboardFlow flow) =>
        new(flow.Id,
            flow.Timestamp,
            flow.Path,
            flow.Method,
            flow.Status,
            null,
            null,
            "",
            null,
            flow.RequestType,
            flow.ResponseType,
            flow.Known,
            "",
            0,
            0,
            null,
            null,
            "",
            null,
            null,
            null,
            null,
            flow.ResponseIsAck,
            null,
            false);
}
