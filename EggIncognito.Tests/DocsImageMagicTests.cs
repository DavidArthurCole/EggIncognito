using System.Text;
using EggIncognito.Controllers;

namespace EggIncognito.Tests;

// The upload ContentType is client-supplied, so DocsController verifies the leading bytes match the
// declared raster format before storing. A non-image payload labeled image/* must be rejected.
public class DocsImageMagicTests
{
    private static byte[] Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];
    private static byte[] Jpeg => [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
    private static byte[] Gif89 => [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x00, 0x00];
    private static byte[] Gif87 => [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7', (byte)'a', 0x00, 0x00];
    // "RIFF" + 4 size bytes + "WEBP" + start of the VP8 chunk.
    private static byte[] Webp =>
    [
        (byte)'R', (byte)'I', (byte)'F', (byte)'F', 0x00, 0x00, 0x00, 0x00,
        (byte)'W', (byte)'E', (byte)'B', (byte)'P', (byte)'V', (byte)'P', (byte)'8', (byte)' ',
    ];

    [Fact]
    public void MagicMatches_AcceptsRealFormats()
    {
        Assert.True(DocsController.MagicMatches(Png, "image/png"));
        Assert.True(DocsController.MagicMatches(Jpeg, "image/jpeg"));
        Assert.True(DocsController.MagicMatches(Gif89, "image/gif"));
        Assert.True(DocsController.MagicMatches(Gif87, "image/gif"));
        Assert.True(DocsController.MagicMatches(Webp, "image/webp"));
    }

    [Fact]
    public void MagicMatches_RejectsMismatchedDeclaredType()
    {
        Assert.False(DocsController.MagicMatches(Png, "image/jpeg"));
        Assert.False(DocsController.MagicMatches(Jpeg, "image/png"));
        Assert.False(DocsController.MagicMatches(Webp, "image/gif"));
    }

    [Fact]
    public void MagicMatches_RejectsNonImagePayloads()
    {
        var html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");
        Assert.False(DocsController.MagicMatches(html, "image/png"));
        Assert.False(DocsController.MagicMatches(html, "image/jpeg"));
        Assert.False(DocsController.MagicMatches(html, "image/gif"));
        Assert.False(DocsController.MagicMatches(html, "image/webp"));
        Assert.False(DocsController.MagicMatches([], "image/png"));
        Assert.False(DocsController.MagicMatches(Png, "image/svg+xml")); // never allowed
    }
}
