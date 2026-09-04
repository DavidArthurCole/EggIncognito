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
        builder.Services.AddSingleton<IDeviceProvisioner, RemoteDeviceProvisioner>();
        builder.Services.AddSingleton<IDeviceProvisioners, DeviceProvisioners>();
        builder.Services.AddSingleton<VirtualDeviceLifecycle>();

        if (RemoteDeviceProvisioner.IsRemoteKind(config.Kind)) return;
        if (!boot.DbEnabled || boot.FakeDevices) return;
        builder.Services.AddScoped<ProvisionedInstanceStore>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<VirtualDeviceLifecycle>());
        builder.Services.AddHostedService<DockerEventWatcher>();

        builder.Services.AddHttpClient(ImageBuilder.HttpClientName, c => {
            c.Timeout = TimeSpan.FromMinutes(10);
            c.MaxResponseContentBufferSize = 512L * 1024 * 1024;
            c.DefaultRequestHeaders.UserAgent.ParseAdd("EggIncognito-ImageBuild/1.0");
        });
        builder.Services.AddSingleton<IImageBuildExecutor, LocalImageBuildExecutor>();
        builder.Services.AddSingleton<ImageBuildRunner>();
        builder.Services.AddScoped<ImageBuilder>();
    }
}
