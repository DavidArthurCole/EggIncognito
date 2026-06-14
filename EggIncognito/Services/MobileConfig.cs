using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services;

// Builds an Apple .mobileconfig (configuration profile) that installs the capture root CA in one tap
// on iOS. Cert only: the global-proxy payload (com.apple.proxy.http.global) is supervised-device only,
// so it cannot be auto-applied on a normal phone; proxy host/port are delivered as text instead.
//
// UUIDs are derived from the cert bytes so the same CA always yields the same profile identity, a
// reinstall replaces cleanly rather than stacking duplicate profiles.
public static class MobileConfig
{
    public static byte[] BuildCaProfile(byte[] cerDer)
    {
        var profileUuid = DeterministicUuid(cerDer, "profile");
        var certUuid = DeterministicUuid(cerDer, "cert");
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
                  <string>me.davidarthurcole.eggincognito.capture.ca</string>
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
              <string>me.davidarthurcole.eggincognito.capture</string>
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

    // A stable RFC-4122-shaped UUID derived from the cert bytes + a role tag. Not a real v5 UUID, just
    // a deterministic 16-byte hash formatted as a UUID, which is all the profile needs.
    internal static string DeterministicUuid(byte[] cerDer, string role)
    {
        var hash = SHA256.HashData([.. cerDer, .. Encoding.UTF8.GetBytes(role)]);
        var g = new byte[16];
        Array.Copy(hash, g, 16);
        return new Guid(g).ToString("D").ToUpperInvariant();
    }
}
