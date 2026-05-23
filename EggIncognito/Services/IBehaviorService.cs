namespace EggIncognito.Services;

public interface IBehaviorService
{
    SimulationBehavior? Get(string name);
    IReadOnlyList<SimulationBehavior> All();
    IReadOnlyList<SimulationBehavior> ForEndpoint(string slug);
}
