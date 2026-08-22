namespace EggIncognito.Models.Playground;

public record LightingDto(
    bool Enabled,
    Vec3Dto? LightDir,
    Vec4Dto? LightDirectColor,
    float LightDirectIntensity,
    Vec4Dto? LightAmbientColor,
    float LightAmbientIntensity,
    Vec4Dto? FogColor,
    float FogNear,
    float FogFar,
    float FogDensity);
