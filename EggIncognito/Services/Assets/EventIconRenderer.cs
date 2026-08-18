using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EggIncognito.Services.Assets;

public static class EventIconRenderer {
    public static byte[] Render(byte[] glyphPng, string eventType, bool ccOnly) {
        using var glyph = Image.Load(glyphPng);
        var hex = EventPalette.ColorFor(eventType);
        var newWidth = (int)(glyph.Width * 1.1);
        var newHeight = (int)(glyph.Height * 1.1);
        using var canvas = new Image<Rgba32>(newWidth, newHeight);
        if (ccOnly) {
            var gradient = new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(newWidth, 0),
                GradientRepetitionMode.None,
                new ColorStop(0, Color.ParseHex(EventPalette.CcGradientFrom)),
                new ColorStop(1, Color.ParseHex(EventPalette.CcGradientTo)));
            canvas.Mutate(ctx => ctx.Fill(gradient));
        } else {
            canvas.Mutate(ctx => ctx.Fill(Color.ParseHex(hex)));
        }
        canvas.Mutate(ctx => ctx.DrawImage(glyph, new Point((newWidth - glyph.Width) / 2, (newHeight - glyph.Height) / 2), 1f));
        using var stream = new MemoryStream();
        canvas.Save(stream, new PngEncoder());
        return stream.ToArray();
    }
}
