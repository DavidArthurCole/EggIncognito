using System.Text;
using EggIncognito.Core.Services.Devices;

namespace EggIncognito.Tests.Devices;

public class KeyboxCodecTests {
    private const string Xml =
        "<?xml version=\"1.0\"?>\n"
        + "<AndroidAttestation>\n"
        + "  <NumberOfKeyboxes>1</NumberOfKeyboxes>\n"
        + "  <Keybox DeviceID=\"egi\">\n"
        + "    <Key algorithm=\"ecdsa\"><PrivateKey format=\"pem\">-----BEGIN EC PRIVATE KEY-----\nMHQCAQEE\n-----END EC PRIVATE KEY-----</PrivateKey></Key>\n"
        + "  </Keybox>\n"
        + "</AndroidAttestation>\n";

    [Fact]
    public void Decode_InvertsEncode() {
        string encoded = KeyboxCodec.Encode(Xml);

        string decoded = KeyboxCodec.Decode(Encoding.ASCII.GetBytes(encoded));

        Assert.Equal(Xml, decoded);
        Assert.DoesNotContain("<Keybox", encoded, StringComparison.Ordinal);
    }

    [Fact]
    public void Decode_ToleratesWhitespaceInEncodedText() {
        string encoded = KeyboxCodec.Encode(Xml);
        string wrapped = string.Join("\n", encoded.Chunk(76).Select(c => new string(c))) + "\n";

        Assert.Equal(Xml, KeyboxCodec.Decode(Encoding.ASCII.GetBytes(wrapped)));
    }

    [Fact]
    public void Decode_Garbage_ThrowsNamingStage() {
        var ex = Assert.Throws<FormatException>(() => KeyboxCodec.Decode(Encoding.ASCII.GetBytes("this is not a keybox!!!")));

        Assert.Contains("base64", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LooksLikeKeybox_DetectsRootElements() {
        Assert.True(KeyboxCodec.LooksLikeKeybox(Xml));
        Assert.True(KeyboxCodec.LooksLikeKeybox("<Keybox DeviceID=\"x\"/>"));
        Assert.False(KeyboxCodec.LooksLikeKeybox("<html></html>"));
    }
}
