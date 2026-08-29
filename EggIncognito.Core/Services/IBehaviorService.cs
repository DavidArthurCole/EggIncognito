namespace EggIncognito.Core.Services;

public interface IBehaviorService {
    SimulationBehavior? Find(string name);
    IReadOnlyList<SimulationBehavior> All();
    IReadOnlyList<SimulationBehavior> ForEndpoint(string slug);
}
