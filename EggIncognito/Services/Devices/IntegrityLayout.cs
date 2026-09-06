using System.Globalization;
using System.Text;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Services.Devices;

public static class IntegrityLayout {
    public const string Package = "com.auxbrain.egginc";
    public const string CaCertsDir = "system/etc/security/cacerts";

    private static readonly UTF8Encoding Utf8 = new(false);

    public static List<IntegrityFile> Plan(IntegrityBundle bundle, string? adbKey, string? caHash, string? caPem) {
        if (!bundle.Ok || bundle.PifPropText is not { } pifProp || bundle.KeyboxXml is not { } keybox
            || bundle.PatchDate is not { } patchDate)
            throw new InvalidOperationException(bundle.Error ?? "integrity assets did not resolve");

        string seedDir = IntegritySeed.SeedDir.TrimStart('/');
        string modulesDir = IntegritySeed.ModulesDir.TrimStart('/');
        var files = new List<IntegrityFile>();
        var moduleNames = new List<string>();
        for (int i = 0; i < bundle.Modules.Count; i++) {
            var module = bundle.Modules[i];
            string name = $"{(i + 1).ToString("00", CultureInfo.InvariantCulture)}-{module.Spec.Name}.zip";
            moduleNames.Add(name);
            files.Add(new IntegrityFile($"{modulesDir}/{name}", module.Zip, false));
        }

        files.Add(Text($"{seedDir}/{PifProp.FileName}", pifProp));
        files.Add(Text($"{seedDir}/{IntegrityChain.KeyboxFileName}", keybox));
        files.Add(Text($"{seedDir}/{IntegrityChain.TargetsFileName}", IntegrityChain.TargetsText(Package)));
        files.Add(Text($"{seedDir}/{IntegrityChain.SecurityPatchFileName}", IntegrityChain.SecurityPatchText(patchDate)));
        if (adbKey is { Length: > 0 } key) {
            files.Add(Text(IntegritySeed.AdbKeysFile.TrimStart('/'), key.Trim() + "\n"));
            files.Add(Text(IntegritySeed.RootAdbKeysFile.TrimStart('/'), key.Trim() + "\n"));
        }

        files.Add(new IntegrityFile(IntegritySeed.SeedScript.TrimStart('/'),
            Utf8.GetBytes(IntegritySeed.Script(moduleNames)), true));
        files.Add(Text(IntegritySeed.RcFile.TrimStart('/'), IntegritySeed.Rc));
        if (caHash is { Length: > 0 } hash && caPem is { Length: > 0 } pem)
            files.Add(Text($"{CaCertsDir}/{hash}.0", pem));

        return files;
    }

    private static IntegrityFile Text(string relativePath, string text) =>
        new(relativePath, Utf8.GetBytes(text.Replace("\r\n", "\n")), false);
}
