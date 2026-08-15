using EggIncognito.Services.Data;
using EggIncognito.Services.Devices;
using EggIncognito.Services.Notifications;
using EggIncognito.Services.Protos;

namespace EggIncognito.Services.Workbench;

public static class WorkbenchRegistration {
    public static IServiceCollection AddWorkbenchStates(this IServiceCollection services) {
        services.AddScoped<ProtoWorkbenchState>();
        services.AddScoped<DeviceWorkbenchState>();
        services.AddScoped<NotificationsWorkbenchState>();
        services.AddScoped<DataWorkbenchState>();
        return services;
    }
}
