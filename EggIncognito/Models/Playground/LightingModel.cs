namespace EggIncognito.Models.Playground;

public sealed class LightingModel {
    public bool Enabled { get; set; }
    public float DirX { get; set; } = 0.5f;
    public float DirY { get; set; } = 0.8f;
    public float DirZ { get; set; } = 0.5f;
    public string DirectColor { get; set; } = "#ffffff";
    public float DirectIntensity { get; set; } = 1.0f;
    public string AmbientColor { get; set; } = "#ffffff";
    public float AmbientIntensity { get; set; } = 0.55f;
    public string FogColor { get; set; } = "#1a1a1f";
    public float FogNear { get; set; }
    public float FogFar { get; set; }
    public float FogDensity { get; set; }
}
