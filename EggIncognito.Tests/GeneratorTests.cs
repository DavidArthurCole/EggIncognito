using EggIncognito.Generator;

namespace EggIncognito.Tests;

public class GeneratorTests
{
    [Fact]
    public void ParsesEndpointsFromYaml()
    {
        const string yaml = """
            endpoints:
              - path: ei/first_contact_secure
                requestType: EggIncFirstContactRequest
                responseType: EggIncFirstContactResponse
              - path: ei/get_periodicals
                requestType: GetPeriodicalsRequest
                responseType: PeriodicalsResponse
            """;

        var endpoints = EndpointParser.Parse(yaml);

        Assert.Equal(2, endpoints.Count);
        Assert.Equal("ei/first_contact_secure", endpoints[0].Path);
        Assert.Equal("EggIncFirstContactRequest", endpoints[0].RequestType);
        Assert.Equal("EggIncFirstContactResponse", endpoints[0].ResponseType);
        Assert.Equal("ei/get_periodicals", endpoints[1].Path);
    }

    [Fact]
    public void StripsTrailingSlashFromPath()
    {
        const string yaml = """
            endpoints:
              - path: ei/get_events/
                responseType: EggIncCurrentEvents
            """;

        var endpoints = EndpointParser.Parse(yaml);

        Assert.Equal("ei/get_events", endpoints[0].Path);
    }

    [Fact]
    public void DefaultsToAuthenticatedMessageWhenTypesOmitted()
    {
        const string yaml = """
            endpoints:
              - path: ei/unknown_endpoint
            """;

        var endpoints = EndpointParser.Parse(yaml);

        Assert.Equal("AuthenticatedMessage", endpoints[0].RequestType);
        Assert.Equal("AuthenticatedMessage", endpoints[0].ResponseType);
    }

    [Theory]
    [InlineData("ei/first_contact_secure", "EiFirstContactSecureController")]
    [InlineData("ei_afx/launch_mission", "EiAfxLaunchMissionController")]
    [InlineData("ei_ctx/get_leaderboard", "EiCtxGetLeaderboardController")]
    [InlineData("ei_data", "EiDataController")]
    public void DerivedClassNameFromPath(string path, string expected)
    {
        Assert.Equal(expected, EndpointParser.ToClassName(path));
    }
}
