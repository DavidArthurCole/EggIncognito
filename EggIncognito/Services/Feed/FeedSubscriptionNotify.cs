using EggIncognito.Services.Admin;

namespace EggIncognito.Services.Feed;

public static class FeedSubscriptionNotify {
    public static void Changed(IServiceProvider services) =>
        (services.GetService(typeof(AdminNotifier)) as AdminNotifier)?.Publish(AdminTopics.Notifications);
}
