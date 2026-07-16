namespace EggIncognito.Bot;
public interface ISyncNotifier
{
   
    Task NotifyAsync(string outcome, CancellationToken ct = default);
}
