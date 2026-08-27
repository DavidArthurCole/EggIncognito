namespace EggIncognito.Capture;

public interface IProcessedFlowObserver {
    void OnFlowProcessed(string deviceId, DashboardFlow flow);
}
