using EggIncognito.Bot;

namespace EggIncognito.Tests;

public class DeployAgentClientTests
{
    [Fact]
    public void Parse_Success_MapsAllFields()
    {
        var r = DeployAgentClient.Parse("""{"ok":true,"fromHash":"aaa","toHash":"bbb"}""");
        Assert.True(r.Ok);
        Assert.False(r.AlreadyUpToDate);
        Assert.Equal("aaa", r.FromHash);
        Assert.Equal("bbb", r.ToHash);
        Assert.Null(r.Tail);
    }

    [Fact]
    public void Parse_AlreadyUpToDate()
    {
        var r = DeployAgentClient.Parse("""{"ok":true,"alreadyUpToDate":true,"fromHash":"aaa","toHash":"aaa"}""");
        Assert.True(r.Ok);
        Assert.True(r.AlreadyUpToDate);
        Assert.Equal("aaa", r.FromHash);
    }

    [Fact]
    public void Parse_Failure_CarriesTail()
    {
        var r = DeployAgentClient.Parse("""{"ok":false,"tail":"docker pull: boom"}""");
        Assert.False(r.Ok);
        Assert.Equal("docker pull: boom", r.Tail);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("")]
    [InlineData("[1,2,3]")]
    [InlineData("null")]
    public void Parse_Garbage_IsFailureWithReason(string body)
    {
        var r = DeployAgentClient.Parse(body);
        Assert.False(r.Ok);
        Assert.Contains("decode", r.Tail);
    }
}
