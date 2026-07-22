using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services;


public static class MobileConfig {


    public static byte[] BuildCaProfile(byte[] cerDer, string stableId) {
        var idSeed = Encoding.UTF8.GetBytes(stableId);
        var profileUuid = DeterministicUuid(idSeed, "profile");
        var certUuid = DeterministicUuid(idSeed, "cert");
        var idSuffix = DeterministicUuid(idSeed, "ident").ToLowerInvariant();
        var certB64 = Convert.ToBase64String(cerDer);

        var xml =
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>PayloadContent</key>
              <array>
                <dict>
                  <key>PayloadCertificateFileName</key>
                  <string>eggincognito-capture-ca.cer</string>
                  <key>PayloadContent</key>
                  <data>
                  {certB64}
                  </data>
                  <key>PayloadDescription</key>
                  <string>EggIncognito capture root CA</string>
                  <key>PayloadDisplayName</key>
                  <string>EggIncognito Capture CA</string>
                  <key>PayloadIdentifier</key>
                  <string>me.davidarthurcole.eggincognito.capture.ca.{idSuffix}</string>
                  <key>PayloadType</key>
                  <string>com.apple.security.root</string>
                  <key>PayloadUUID</key>
                  <string>{certUuid}</string>
                  <key>PayloadVersion</key>
                  <integer>1</integer>
                </dict>
              </array>
              <key>PayloadDescription</key>
              <string>Installs the EggIncognito capture root CA so Egg, Inc. HTTPS can be decrypted.</string>
              <key>PayloadDisplayName</key>
              <string>EggIncognito Capture</string>
              <key>PayloadIdentifier</key>
              <string>me.davidarthurcole.eggincognito.capture.{idSuffix}</string>
              <key>PayloadOrganization</key>
              <string>EggIncognito</string>
              <key>PayloadRemovalDisallowed</key>
              <false/>
              <key>PayloadType</key>
              <string>Configuration</string>
              <key>PayloadUUID</key>
              <string>{profileUuid}</string>
              <key>PayloadVersion</key>
              <integer>1</integer>
            </dict>
            </plist>
            """;
        return Encoding.UTF8.GetBytes(xml);
    }


    internal static string DeterministicUuid(byte[] seed, string role) {
        var hash = SHA256.HashData([.. seed, .. Encoding.UTF8.GetBytes(role)]);
        var g = new byte[16];
        Array.Copy(hash, g, 16);
        return new Guid(g).ToString("D").ToUpperInvariant();
    }
}
