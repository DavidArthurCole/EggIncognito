using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class CaInstallerTests
{
    sealed class FakeRunner(Func<string, string[], ProcessResult> fn) : IProcessRunner
    {
        public readonly List<(string exe, string[] args)> Calls = [];
        public Task<ProcessResult> RunAsync(string exe, string[] args, CancellationToken ct)
        {
            Calls.Add((exe, args));
            return Task.FromResult(fn(exe, args));
        }
    }

    // A self-signed CA written to a temp DER file, mirroring what the proxy exports at caPath.
    static (string path, X509Certificate2 cert) MakeCa()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=EggIncognito Capture CA, O=EggIncognito", rsa,
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        var cert = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));
        var path = Path.Combine(Path.GetTempPath(), $"egi-ca-test-{Guid.NewGuid():N}.cer");
        File.WriteAllBytes(path, cert.Export(X509ContentType.Cert));
        // Re-load from the exported DER so the test exercises the same load path the installer uses.
        return (path, X509CertificateLoader.LoadCertificateFromFile(path));
    }

    [Fact]
    public void AndroidSubjectHashOld_Is8HexDigits_AndStable()
    {
        var (path, cert) = MakeCa();
        try
        {
            var h = CaCertPrep.AndroidSubjectHashOld(cert);
            Assert.Matches("^[0-9a-f]{8}$", h);
            Assert.Equal(h, CaCertPrep.AndroidSubjectHashOld(cert)); // deterministic
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Pem_RoundTrips_ToSameCert()
    {
        var (path, cert) = MakeCa();
        try
        {
            var pem = CaCertPrep.ToPem(cert);
            Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem);
            var back = X509Certificate2.CreateFromPem(pem);
            Assert.Equal(cert.Thumbprint, back.Thumbprint);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IosHashes_MatchKnownAlgorithms()
    {
        var (path, cert) = MakeCa();
        try
        {
            var sha1 = CaCertPrep.IosCertSha1Hex(cert);
            Assert.Equal(Convert.ToHexString(SHA1.HashData(cert.RawData)).ToLowerInvariant(), sha1);
            var subj = CaCertPrep.IosSubjectDerHex(cert);
            Assert.Equal(Convert.ToHexString(cert.SubjectName.RawData).ToLowerInvariant(), subj);
            var data = CaCertPrep.DerHex(cert);
            Assert.Equal(Convert.ToHexString(cert.RawData).ToLowerInvariant(), data);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Adb_PushesPemAndScript_ThenRunsItAsRoot()
    {
        var (path, cert) = MakeCa();
        try
        {
            // "live: mounted" is the real success signal (live cacerts copy works now, no reboot).
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "diag module: written\ndiag live: mounted into running cacerts\ndiag done", ""));
            var inst = new AdbCaInstaller(runner);
            var (ok, note) = await inst.InstallAsync(new DeviceCaTarget("d", "android", "SERIAL"), path, default);

            Assert.True(ok);
            // Only the Magisk-install script is pushed (the cert travels inline as base64, no separate PEM push).
            var pushes = runner.Calls.Where(c => c.args.Contains("push")).ToList();
            Assert.Single(pushes);
            Assert.Contains("SERIAL", pushes[0].args);
            // The script runs by PATH in the GLOBAL mount ns via `su -mm -c "sh <path> 2>&1"` (so the live
            // cacerts copy is visible to app processes), as root.
            var run = runner.Calls.Single(c => c.args.Contains("su"));
            Assert.Contains("-mm", run.args);
            Assert.Contains(run.args, a => a.Contains("/data/local/tmp/eggincognito-ca-magisk.sh"));
            var hash = CaCertPrep.AndroidSubjectHashOld(cert);
            Assert.Contains(hash, note!);
            Assert.Contains("trusted (live)", note!);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Adb_PushFailure_ReturnsFalse_AndSkipsScript()
    {
        var (path, _) = MakeCa();
        try
        {
            var runner = new FakeRunner((_, args) =>
                args.Contains("push") ? new ProcessResult(1, "", "no device") : new ProcessResult(0, "", ""));
            var inst = new AdbCaInstaller(runner);
            var (ok, note) = await inst.InstallAsync(new DeviceCaTarget("d", "android", "S"), path, default);

            Assert.False(ok);
            Assert.Contains("push", note!);
            Assert.DoesNotContain(runner.Calls, c => c.args.Contains("su")); // never ran the root script
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ios_RunsSqliteInsert_WithBlobsAndStoreSubstituted()
    {
        var (path, cert) = MakeCa();
        try
        {
            // The corrected iOS-16 installer SELECTs the row back; success requires "row-present" in stdout
            // (the old `; echo ok` masked failures). Simulate a verified install.
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "row-present", ""));
            var ssh = new IosCaInstaller.SshConfig("1.2.3.4", "2222", "/k", null, null);
            var inst = new IosCaInstaller(runner, ssh);
            var (ok, _) = await inst.InstallAsync(new DeviceCaTarget("d", "ios", "UDID"), path, default);

            Assert.True(ok);
            var call = runner.Calls.Single(c => c.exe == "ssh");
            var remote = call.args[^1];
            // iOS 16 tsettings keys on sha256, not sha1; default store moved under /var/protected/trustd.
            Assert.Contains(CaCertPrep.IosCertSha256Hex(cert), remote);
            Assert.Contains(CaCertPrep.DerHex(cert), remote);
            Assert.Contains("/private/var/protected/trustd/private/TrustStore.sqlite3", remote); // default store
            Assert.Contains("sha256", remote);
            Assert.Contains("killall -9 trustd", remote);
            Assert.DoesNotContain("{sha256}", remote);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ios_NoSshCreds_ReturnsFalse()
    {
        var (path, _) = MakeCa();
        try
        {
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
            var inst = new IosCaInstaller(runner, new IosCaInstaller.SshConfig(null, "2222", null, null, null));
            var (ok, note) = await inst.InstallAsync(new DeviceCaTarget("d", "ios", "U"), path, default);
            Assert.False(ok);
            Assert.Contains("ssh", note!);
            Assert.Empty(runner.Calls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Ios_CustomStorePath_IsUsed()
    {
        var (path, _) = MakeCa();
        try
        {
            var runner = new FakeRunner((_, _) => new ProcessResult(0, "", ""));
            var ssh = new IosCaInstaller.SshConfig("h", "22", "/k", null, "/custom/TrustStore.sqlite3");
            var inst = new IosCaInstaller(runner, ssh);
            await inst.InstallAsync(new DeviceCaTarget("d", "ios", "U"), path, default);
            var remote = runner.Calls.Single(c => c.exe == "ssh").args[^1];
            Assert.Contains("/custom/TrustStore.sqlite3", remote);
        }
        finally { File.Delete(path); }
    }
}
