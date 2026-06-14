using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.Services;

// Builds an Apple .mobileconfig (configuration profile) that installs the capture root CA in one tap
// on iOS. The CA profile is cert only: the proxy host/port are delivered as text instead, since the
// per-user IPv6 address identifies the user at the front door (no proxy credentials needed).
//
// UUIDs are derived from the seed bytes so the same input always yields the same profile identity, a
// reinstall replaces cleanly rather than stacking duplicate profiles.
public static class MobileConfig
{
    // stableId (the user's Discord id) anchors the profile + cert payload UUIDs and the PayloadIdentifier
    // so a given user always gets the SAME profile identity. iOS then REPLACES the installed profile when
    // the cert is reinstalled, instead of stacking a new "EggIncognito Capture" each session. The cert
    // bytes still update inside that one profile. Per-user identifiers also keep distinct users separate.
    public static byte[] BuildCaProfile(byte[] cerDer, string stableId)
    {
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

    // A stable RFC-4122-shaped UUID derived from a seed (cert bytes or other input) + a role tag. Not a
    // real v5 UUID, just a deterministic 16-byte hash formatted as a UUID, which is all the profile needs.
    internal static string DeterministicUuid(byte[] seed, string role)
    {
        var hash = SHA256.HashData([.. seed, .. Encoding.UTF8.GetBytes(role)]);
        var g = new byte[16];
        Array.Copy(hash, g, 16);
        return new Guid(g).ToString("D").ToUpperInvariant();
    }
}
