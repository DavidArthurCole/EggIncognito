using System.Globalization;
using Ei;
using Google.Protobuf.Reflection;

namespace EggIncognito.Services;

public static class ProtoEnumNames {
    private static readonly EnumDescriptor ArtifactName = Nested("Name");
    private static readonly EnumDescriptor ArtifactLevel = Nested("Level");
    private static readonly EnumDescriptor ArtifactRarity = Nested("Rarity");
    private static readonly EnumDescriptor Reward = TopLevel("RewardType");

    public static string SpecName(ArtifactSpec.Types.Name name) => Of(ArtifactName, (int)name);

    public static string LevelName(ArtifactSpec.Types.Level level) => Of(ArtifactLevel, (int)level);

    public static string RarityName(ArtifactSpec.Types.Rarity rarity) => Of(ArtifactRarity, (int)rarity);

    public static string RewardTypeName(RewardType type) => Of(Reward, (int)type);

    private static EnumDescriptor Nested(string name) =>
        ArtifactSpec.Descriptor.EnumTypes.First(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    private static EnumDescriptor TopLevel(string name) =>
        EiReflection.Descriptor.EnumTypes.First(e => string.Equals(e.Name, name, StringComparison.Ordinal));

    private static string Of(EnumDescriptor descriptor, int number) =>
        descriptor.FindValueByNumber(number)?.Name ?? number.ToString(CultureInfo.InvariantCulture);
}
