using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace EggIncognito.Services.Assets;

public static class EventIconRenderer {
    public static byte[] Render(byte[] glyphPng, string eventType, bool ccOnly) {
        using var glyph = Image.Load(glyphPng);
        var hex = eventType.ToLowerInvariant().Replace('_', '-') switch {
            "epic-research-sale" => "#ef4444",
            "piggy-boost" => "#f97316",
            "piggy-cap-boost" => "#f59e0b",
            "prestige-boost" => "#f59e0b",
            "earnings-boost" => "#84cc16",
            "gift-boost" => "#10b981",
            "drone-boost" => "#10b981",
            "research-sale" => "#14b8a6",
            "hab-sale" => "#06b6d4",
            "vehicle-sale" => "#0ea5e9",
            "boost-sale" => "#3b82f6",
            "boost-duration" => "#6366f1",
            "crafting-sale" => "#8b5cf6",
            "mission-fuel" => "#8b5cf6",
            "mission-capacity" => "#d946ef",
            "mission-duration" => "#ec4899",
            "shell-sale" => "#f43f5e",
            _ => "#9ca3af"
        };
        var newWidth = (int)(glyph.Width * 1.1);
        var newHeight = (int)(glyph.Height * 1.1);
        using var canvas = new Image<Rgba32>(newWidth, newHeight);
        if (ccOnly) {
            var gradient = new LinearGradientBrush(
                new PointF(0, 0),
                new PointF(newWidth, 0),
                GradientRepetitionMode.None,
                new ColorStop(0, Color.ParseHex("#f5a709")),
                new ColorStop(1, Color.ParseHex("#900fb1")));
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
