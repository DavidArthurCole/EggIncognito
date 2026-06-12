using System.Collections.Generic;
using System.Linq;
using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class CommandSignatureTests
{
    private static OptionShape Opt(string name, bool required = false, bool autocomplete = false,
        IReadOnlyList<OptionShape>? options = null) =>
        new(name, name + " desc", 3, required, autocomplete, options ?? new List<OptionShape>());

    private static CommandShape Cmd(string name, params OptionShape[] options) =>
        new(name, name + " desc", options);

    [Fact]
    public void Compute_IsOrderInsensitiveByCommandName()
    {
        var a = CommandSignature.Compute(new[] { Cmd("alpha"), Cmd("beta") });
        var b = CommandSignature.Compute(new[] { Cmd("beta"), Cmd("alpha") });
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenDescriptionChanges()
    {
        var a = CommandSignature.Compute(new[] { new CommandShape("x", "one", new List<OptionShape>()) });
        var b = CommandSignature.Compute(new[] { new CommandShape("x", "two", new List<OptionShape>()) });
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Compute_ChangesWhenOptionAddedOrFlagged()
    {
        var bare = CommandSignature.Compute(new[] { Cmd("x") });
        var withOpt = CommandSignature.Compute(new[] { Cmd("x", Opt("page")) });
        var withRequired = CommandSignature.Compute(new[] { Cmd("x", Opt("page", required: true)) });
        Assert.NotEqual(bare, withOpt);
        Assert.NotEqual(withOpt, withRequired);
    }

    [Fact]
    public void Compute_CapturesNestedOptions()
    {
        var flat = CommandSignature.Compute(new[] { Cmd("x", Opt("sub")) });
        var nested = CommandSignature.Compute(new[]
        {
            Cmd("x", Opt("sub", options: new List<OptionShape> { Opt("name", required: true, autocomplete: true) })),
        });
        Assert.NotEqual(flat, nested);
    }

    [Fact]
    public void FromProperties_CapturesTheCatalogShape()
    {
        var shapes = CommandDefinitions.BuildAll().Select(CommandSignature.FromProperties).ToList();
        Assert.Equal(5, shapes.Count);

        var protoCmd = Assert.Single(shapes, s => s.Name == "proto");
        Assert.Equal(2, protoCmd.Options.Count);

        var typeSub = Assert.Single(protoCmd.Options, o => o.Name == "type");
        var nameOpt = Assert.Single(typeSub.Options, o => o.Name == "name");
        Assert.True(nameOpt.Required);
        Assert.True(nameOpt.Autocomplete);

        var listSub = Assert.Single(protoCmd.Options, o => o.Name == "list");
        var pageOpt = Assert.Single(listSub.Options, o => o.Name == "page");
        Assert.False(pageOpt.Required);
    }

    [Fact]
    public void Compute_OnTheRealCatalog_IsDeterministic()
    {
        string Sig() => CommandSignature.Compute(
            CommandDefinitions.BuildAll().Select(CommandSignature.FromProperties));
        Assert.Equal(Sig(), Sig());
    }
}
