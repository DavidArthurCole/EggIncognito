using EggIdentity.Deploy;
using EggIdentity.Deploy.AdminUi;
using EggIncognito.Services.Admin;

namespace EggIncognito.Startup;

public static class DeployServices {
    public static void AddDeployServices(this WebApplicationBuilder builder) {
        builder.Services.AddEggIdentityDeployFromEnvironment("eggincognito");
        if (!builder.Services.Any(d => d.ServiceType == typeof(IDeployEvents))) return;

        builder.Services.AddEggIdentityDeployToasts();
        builder.Services.AddHostedService<DeployAdminBridge>();
    }
}
