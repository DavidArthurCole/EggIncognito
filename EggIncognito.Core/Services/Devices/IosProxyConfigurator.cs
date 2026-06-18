namespace EggIncognito.Core.Services.Devices;

// Points a jailbroken iOS device's HTTP proxy at the capture listener over ssh. iOS stores the proxy in
// the active network service inside SystemConfiguration/preferences.plist; the exact write differs by iOS
// version and jailbreak, so the set/clear commands are TEMPLATES supplied by config (IosSshProxy:SetCommand
// / ClearCommand) with {host}/{port} placeholders. This keeps the mechanism tunable on-device (one-attempt-
// then-verify) without a code change. The capture CA is already trusted on a jailbroken device.
//
// ssh creds (host/port/key) come from the same config the iOS updater + binary puller use, passed in by
// the caller as SshConfig. device.Target is the UDID (not used for ssh; the ssh host is the LAN address).
// Never throws: a non-zero ssh exit or an unconfigured template returns (false, note).
public sealed class IosProxyConfigurator(IProcessRunner runner, IosProxyConfigurator.SshConfig ssh) : IDeviceProxyConfigurator
{
    public sealed record SshConfig(string? Host, string Port, string? KeyPath, string? SetTemplate, string? ClearTemplate);

    public string Platform => "ios";

    public async Task<(bool Ok, string? Note)> SetProxyAsync(DeviceProxyTarget device, string hostIp, int port, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ssh.SetTemplate))
            return (false, "ios proxy SetCommand template not configured (DeviceCapture:Ios:SetCommand)");
        var cmd = ssh.SetTemplate.Replace("{host}", hostIp).Replace("{port}", port.ToString());
        return await Ssh(cmd, ct);
    }

    public async Task<(bool Ok, string? Note)> ClearProxyAsync(DeviceProxyTarget device, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ssh.ClearTemplate))
            return (false, "ios proxy ClearCommand template not configured (DeviceCapture:Ios:ClearCommand)");
        return await Ssh(ssh.ClearTemplate, ct);
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
