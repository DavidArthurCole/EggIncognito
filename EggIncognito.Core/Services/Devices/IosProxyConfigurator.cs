namespace EggIncognito.Core.Services.Devices;



public sealed class IosProxyConfigurator(IProcessRunner runner, IosProxyConfigurator.SshConfig ssh) : IDeviceProxyConfigurator
{
   
   
    public sealed record SshConfig(
        string? Host, string Port, string? KeyPath, string? SetTemplate, string? ClearTemplate,
        string? Guid = null, string? PlutilPath = null, string? PrefsPlist = null);

    private const string DefaultPlutil = "/cores/binpack/usr/bin/plutil";
    private const string DefaultPrefs = "/var/preferences/SystemConfiguration/preferences.plist";

    public string Platform => "ios";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceProxyTarget device, string hostIp, int port, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(ssh.SetTemplate))
            return await Ssh(ssh.SetTemplate.Replace("{host}", hostIp).Replace("{port}", port.ToString()), ct);
        if (string.IsNullOrEmpty(ssh.Guid))
            return (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)");
        return await Ssh(BuildSet(hostIp, port), ct);
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceProxyTarget device, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(ssh.ClearTemplate))
            return await Ssh(ssh.ClearTemplate, ct);
        if (string.IsNullOrEmpty(ssh.Guid))
            return (false, "ios proxy needs the network-service guid (DeviceCapture:Ios:NetworkServiceGuid)");
        return await Ssh(BuildClear(), ct);
    }

   
   
    internal string BuildSet(string hostIp, int port)
    {
        var p = ssh.PlutilPath ?? DefaultPlutil;
        var f = ssh.PrefsPlist ?? DefaultPrefs;
        string Set(string key, string value, string type) =>
            $"{p} -key NetworkServices -key {ssh.Guid} -key Proxies -key {key} -value {value} -type {type} {f}";
        return string.Join("; ", new[]
        {
            Set("HTTPEnable", "1", "int"), Set("HTTPProxy", hostIp, "string"), Set("HTTPPort", port.ToString(), "int"),
            Set("HTTPSEnable", "1", "int"), Set("HTTPSProxy", hostIp, "string"), Set("HTTPSPort", port.ToString(), "int"),
        });
    }

    internal string BuildClear()
    {
        var p = ssh.PlutilPath ?? DefaultPlutil;
        var f = ssh.PrefsPlist ?? DefaultPrefs;
        string Disable(string key) =>
            $"{p} -key NetworkServices -key {ssh.Guid} -key Proxies -key {key} -value 0 -type int {f}";
        return $"{Disable("HTTPEnable")}; {Disable("HTTPSEnable")}";
    }

    private async Task<(bool Ok, string? Note)> Ssh(string remoteCmd, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ssh.Host) || string.IsNullOrEmpty(ssh.KeyPath))
            return (false, "ios ssh host/key not configured");
        var r = await runner.RunAsync("ssh",
            ["-p", ssh.Port, "-i", ssh.KeyPath, "-o", "StrictHostKeyChecking=no", "-o", "BatchMode=yes",
             $"root@{ssh.Host}", remoteCmd], ct);
        return r.ExitCode == 0 ? (true, null) : (false, DeviceParsing.TrimNote(r.Stderr + r.Stdout));
    }
}
