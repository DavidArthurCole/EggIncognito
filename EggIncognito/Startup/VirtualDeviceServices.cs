using EggIncognito.Core.Services.Devices;
using EggIncognito.Data.Services;
using EggIncognito.Services.Devices;

namespace EggIncognito.Startup;

public static class VirtualDeviceServices {
    public static void AddVirtualDeviceServices(this WebApplicationBuilder builder, BootFlags boot) {
        var config = VirtualDeviceConfig.Bind(builder.Configuration);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(_ => new DockerEngineClient(config.DockerSocket));
        builder.Services.AddSingleton<IDeviceProvisioner, RedroidProvisioner>();
        builder.Services.AddSingleton<IDeviceProvisioners, DeviceProvisioners>();
        builder.Services.AddSingleton<VirtualDeviceLifecycle>();

        if (!boot.DbEnabled || boot.FakeDevices) return;
        builder.Services.AddScoped<ProvisionedInstanceStore>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<VirtualDeviceLifecycle>());
    }
}
