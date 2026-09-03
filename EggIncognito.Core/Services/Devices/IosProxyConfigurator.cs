using System.Globalization;

namespace EggIncognito.Core.Services.Devices;

public sealed class IosProxyConfigurator(IProcessRunner runner, IosProxyConfigurator.SshConfig ssh)
    : IDeviceProxyConfigurator {
    private const string DefaultPlutil = "/cores/binpack/usr/bin/plutil";
    private const string DefaultPrefs = "/var/preferences/SystemConfiguration/preferences.plist";

    public string Platform => "ios";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceTarget device, string hostIp, int port,
        CancellationToken ct) {
        return !string.IsNullOrEmpty(ssh.SetTemplate)
            ? await Ssh(
                ssh.SetTemplate.Replace("{host}", hostIp)
                    .Replace("{port}", port.ToString(CultureInfo.InvariantCulture)), ct)
            : string.IsNullOrEmpty(ssh.NetworkServiceGuid)
                ? (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)")
                : await Ssh(BuildSet(hostIp, port), ct);
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceTarget device, CancellationToken ct) {
        return !string.IsNullOrEmpty(ssh.ClearTemplate)
            ? await Ssh(ssh.ClearTemplate, ct)
            : string.IsNullOrEmpty(ssh.NetworkServiceGuid)
                ? (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)")
                : await Ssh(BuildClear(), ct);
    }

    internal string BuildSet(string hostIp, int port) {
        string p = ssh.PlutilPath ?? DefaultPlutil;
        string f = ssh.PrefsPlist ?? DefaultPrefs;

        string Set(string key, string value, string type) {
            return $"{p} -key NetworkServices -key {ssh.NetworkServiceGuid} -key Proxies -key {key} -value {value} -type {type} {f}";
        }

        string portStr = port.ToString(CultureInfo.InvariantCulture);
        return string.Join("; ", Set("HTTPEnable", "1", "int"), Set("HTTPProxy", hostIp, "string"),
            Set("HTTPPort", portStr, "int"), Set("HTTPSEnable", "1", "int"), Set("HTTPSProxy", hostIp, "string"),
            Set("HTTPSPort", portStr, "int"));
    }

    internal string BuildClear() {
        string p = ssh.PlutilPath ?? DefaultPlutil;
        string f = ssh.PrefsPlist ?? DefaultPrefs;

        string Disable(string key) {
            return $"{p} -key NetworkServices -key {ssh.NetworkServiceGuid} -key Proxies -key {key} -value 0 -type int {f}";
        }

        return $"{Disable("HTTPEnable")}; {Disable("HTTPSEnable")}";
    }

    private async Task<(bool Ok, string? Note)> Ssh(string remoteCmd, CancellationToken ct) {
        if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.KeyPath))
            return (false, "ios ssh host/key not configured");
        var r = await runner.RunAsync("ssh", new SshEndpoint(ssh.Host, ssh.Port, ssh.KeyPath).SshArgs(remoteCmd), ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }

    public sealed record SshConfig(
        string? Host,
        string Port,
        string? KeyPath,
        string? SetTemplate,
        string? ClearTemplate,
        string? NetworkServiceGuid = null,
        string? PlutilPath = null,
        string? PrefsPlist = null);
}
