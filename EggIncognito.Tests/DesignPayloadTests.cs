using System.Text.Json;

namespace EggIncognito.Tests;

// The design payload is opaque app JSON saved to env_designs.payload. This pins the contract the client
// serializes + the server stores verbatim: an element carries id/kind/ref/transform, lighting carries sun +
// ambient. A round-trip through System.Text.Json must preserve every field.
public class DesignPayloadTests
{
    private sealed record Element(string Id, string Kind, string Ref, string? Hat, string? Shell,
        float[] Pos, float[] RotDeg, float Scale);
    private sealed record Lighting(float SunAzimuthDeg, float SunElevationDeg, string SunColor,
        float SunIntensity, string AmbientColor, float AmbientIntensity);
    private sealed record Payload(string Background, bool BackgroundTransparent, Lighting Lighting, Element[] Elements);

    [Fact]
    public void Payload_RoundTrips()
    {
        var p = new Payload("#102030", false,
            new Lighting(45, 55, "#ffffff", 1.0f, "#222233", 0.4f),
            [
                new Element("el1", "env", "ei_silo_0_large", null, "ei_silo_shell_x", [1, 0, 2], [0, 90, 0], 1.5f),
                new Element("el2", "chicken", "ei_chicken_base", "ei_hat_beret_black", null, [0, 0, 0], [0, 0, 0], 1f),
            ]);

        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<Payload>(json)!;

        Assert.Equal(p.Background, back.Background);
        Assert.False(back.BackgroundTransparent);
        Assert.Equal(90, back.Elements[0].RotDeg[1]);
        Assert.Equal("ei_silo_shell_x", back.Elements[0].Shell);
        Assert.Equal("ei_hat_beret_black", back.Elements[1].Hat);
        Assert.Equal(0.4f, back.Lighting.AmbientIntensity);
    }
}
