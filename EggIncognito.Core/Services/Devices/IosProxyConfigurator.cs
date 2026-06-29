namespace EggIncognito.Core.Services.Devices;

// Points a jailbroken iOS device's HTTP proxy at the capture listener over ssh. iOS stores the proxy in the
// active network service inside SystemConfiguration/preferences.plist. The set/clear commands are BUILT in
// code from a few values (the network-service GUID, the plutil path, the plist path) so the container only
// supplies the GUID, not a 600-char shell script. A full SetTemplate/ClearTemplate ({host}/{port}) remains an
// optional escape hatch for an exotic jailbreak. The capture CA is already trusted on a jailbroken device.
//
// ssh creds (host/port/key) come from the same config the iOS updater + binary puller use, passed in by
// the caller as SshConfig. device.Target is the UDID (not used for ssh; the ssh host is the LAN address).
// Never throws: a non-zero ssh exit or missing config returns (false, note).
public sealed class IosProxyConfigurator(IProcessRunner runner, IosProxyConfigurator.SshConfig ssh) : IDeviceProxyConfigurator
{
    // Guid = the active network-service id under NetworkServices in preferences.plist (the one value that is
    // genuinely device-specific). PlutilPath/PrefsPlist default to the palera1n binpack + standard plist when
    // unset. SetTemplate/ClearTemplate override the whole built command when present (legacy escape hatch).
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

    // plutil writes one key per invocation; the proxy needs the HTTP + HTTPS enable/host/port sextet. Built
    // from the configured guid + plutil/plist paths. internal for the command-shape unit test.
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
