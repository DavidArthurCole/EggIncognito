using System.Text.Json;

namespace EggIncognito.Tests;

public class DesignPayloadTests {
    private sealed record Element(string Id, string Kind, string Ref, string? Hat, string? Shell,
        float[] Pos, float[] RotDeg, float Scale, string Anim);
    private sealed record Lighting(float SunAzimuthDeg, float SunElevationDeg, string SunColor,
        float SunIntensity, string FogColor, float FogDensity);
    private sealed record Payload(string Background, bool BackgroundTransparent, Lighting Lighting, Element[] Elements);

    [Fact]
    public void Payload_RoundTrips() {
        var p = new Payload("#102030", false,
            new Lighting(45, 55, "#ffffff", 1.0f, "#222233", 0.05f),
            [
                new Element("el1", "env", "ei_silo_0_large", null, "ei_silo_shell_x", [1, 0, 2], [0, 90, 0], 1.5f, "SpinY"),
                new Element("el2", "chicken", "ei_chicken_base", "ei_hat_beret_black", null, [0, 0, 0], [0, 0, 0], 1f, "none"),
            ]);

        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<Payload>(json)!;

        Assert.Equal(p.Background, back.Background);
        Assert.False(back.BackgroundTransparent);
        Assert.Equal(90, back.Elements[0].RotDeg[1]);
        Assert.Equal("SpinY", back.Elements[0].Anim);
        Assert.Equal("ei_silo_shell_x", back.Elements[0].Shell);
        Assert.Equal("ei_hat_beret_black", back.Elements[1].Hat);
        Assert.Equal(0.05f, back.Lighting.FogDensity);
    }
}
