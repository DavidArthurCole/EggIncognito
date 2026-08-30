using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace EggIncognito.Services.Devices;

public static class DeviceFrameEncoder {
    public const int DefaultQuality = 75;
    public const int MinQuality = 30;
    public const int MaxQuality = 95;

    public static int ClampQuality(int quality) => Math.Clamp(quality, MinQuality, MaxQuality);

    public static async Task<byte[]?> ToJpegAsync(byte[] source, int quality, CancellationToken ct) {
        try {
            using var image = Image.Load(source);
            using var buffer = new MemoryStream(source.Length / 4);
            await image.SaveAsJpegAsync(buffer, new JpegEncoder { Quality = ClampQuality(quality) }, ct);
            return buffer.ToArray();
        } catch (Exception ex) when (ex is ImageFormatException or NotSupportedException) {
            return null;
        }
    }
}
