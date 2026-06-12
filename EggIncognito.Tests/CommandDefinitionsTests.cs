using System.Linq;
using Discord;
using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class CommandDefinitionsTests
{
    [Fact]
    public void BuildAll_HasExpectedCommands()
    {
        var names = CommandDefinitions.BuildAll().Select(c => c.Name.Value).ToList();
        Assert.Contains("health", names);
        Assert.Contains("status", names);
        Assert.Contains("verify", names);
        Assert.Contains("endpoints", names);
        Assert.Contains("proto", names);
        Assert.Contains("updateserver", names);
    }

    [Fact]
    public void ReadOnlyCommands_AreUserInstallable_AndRunInDms()
    {
        // updateserver is deliberately guild-only (admin-gated); every other command is universal.
        foreach (var c in CommandDefinitions.BuildAll().Where(c => c.Name.Value != "updateserver"))
        {
            Assert.True(c.IntegrationTypes.IsSpecified);
            Assert.Contains(ApplicationIntegrationType.UserInstall, c.IntegrationTypes.Value);
            Assert.Contains(ApplicationIntegrationType.GuildInstall, c.IntegrationTypes.Value);
            Assert.True(c.ContextTypes.IsSpecified);
            Assert.Contains(InteractionContextType.BotDm, c.ContextTypes.Value);
            Assert.Contains(InteractionContextType.PrivateChannel, c.ContextTypes.Value);
            Assert.Contains(InteractionContextType.Guild, c.ContextTypes.Value);
        }
    }

    [Fact]
    public void UpdateServer_IsGuildOnly_AndAdminGated()
    {
        var c = CommandDefinitions.BuildAll().Single(c => c.Name.Value == "updateserver");
        Assert.True(c.IntegrationTypes.IsSpecified);
        Assert.Equal(new[] { ApplicationIntegrationType.GuildInstall }, c.IntegrationTypes.Value);
        Assert.True(c.ContextTypes.IsSpecified);
        Assert.Equal(new[] { InteractionContextType.Guild }, c.ContextTypes.Value);
        Assert.True(c.DefaultMemberPermissions.IsSpecified);
        Assert.Equal(GuildPermission.Administrator, c.DefaultMemberPermissions.Value);
    }
}
