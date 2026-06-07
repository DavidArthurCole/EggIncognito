using EggIncognito.Generator;

namespace EggIncognito.Tests;

public class GeneratorTests
{
    [Fact]
    public void ParsesEndpointsFromYaml()
    {
        const string yaml = """
            routes:
              - path: ei/first_contact_secure
                requestType: EggIncFirstContactRequest
                responseType: EggIncFirstContactResponse
              - path: ei/get_periodicals
                requestType: GetPeriodicalsRequest
                responseType: PeriodicalsResponse
            """;

        var endpoints = RouteParser.Parse(yaml);

        Assert.Equal(2, endpoints.Count);
        Assert.Equal("ei/first_contact_secure", endpoints[0].Path);
        Assert.Equal("EggIncFirstContactRequest", endpoints[0].Request);
        Assert.Equal("EggIncFirstContactResponse", endpoints[0].Response);
        Assert.Equal("ei/get_periodicals", endpoints[1].Path);
    }

    [Fact]
    public void NewSchemaKeysWinAndModelTheThreeAxes()
    {
        const string yaml = """
            routes:
              - path: ei/first_contact_secure
                request: EggIncFirstContactRequest
                requestWrapped: true
                response: EggIncFirstContactResponse
                responseWrapped: true
              - path: ei_srv/subscription_status
                response: UserSubscriptionInfo
                responseWrapped: true
                pathParam: true
                pathParamOnly: true
            """;

        var e = RouteParser.Parse(yaml);

        Assert.Equal("EggIncFirstContactRequest", e[0].Request);
        Assert.True(e[0].RequestWrapped);
        Assert.True(e[0].ResponseWrapped);

        // Path-param-only: no request body, identity via URL.
        Assert.Null(e[1].Request);
        Assert.True(e[1].PathParamOnly);
        Assert.Equal("UserSubscriptionInfo", e[1].Response);
        Assert.True(e[1].ResponseWrapped);
        // Mock still serializes the known inner type.
        Assert.Equal("UserSubscriptionInfo", e[1].MockResponseType);
    }

    [Fact]
    public void InlineCommentsAreStrippedFromValues()
    {
        const string yaml = """
            routes:
              - path: ei/ack_endpoint
                request: SomeRequest
                response:  # ack - AuthenticatedMessage-wrapped, no body
                responseWrapped: true
              - path: ei/needs_capture
                request:  # NEEDS CAPTURE - signed, unknown
                requestWrapped: true
                response: KnownResponse
            """;

        var e = RouteParser.Parse(yaml);

        // Empty value with a trailing comment must read as null, not the comment text.
        Assert.Equal("SomeRequest", e[0].Request);
        Assert.Null(e[0].Response);
        Assert.Equal("AuthenticatedMessage", e[0].MockResponseType); // falls back, never "# ack..."
        Assert.True(e[0].ResponseWrapped);

        Assert.Null(e[1].Request);
        Assert.True(e[1].RequestWrapped);
        Assert.Equal("KnownResponse", e[1].Response);
    }

    [Fact]
    public void LegacyAuthenticatedMessageNormalizesToWrappedUnknown()
    {
        const string yaml = """
            routes:
              - path: ei/get_events
                requestType: AuthenticatedMessage
                responseType: EggIncCurrentEvents
            """;

        var e = RouteParser.Parse(yaml)[0];

        // Legacy AuthenticatedMessage request => unknown inner type + wrapped.
        Assert.Null(e.Request);
        Assert.True(e.RequestWrapped);
        Assert.Equal("EggIncCurrentEvents", e.Response);
        Assert.False(e.ResponseWrapped);
    }

    [Fact]
    public void StripsTrailingSlashFromPath()
    {
        const string yaml = """
            routes:
              - path: ei/get_events/
                responseType: EggIncCurrentEvents
            """;

        var endpoints = RouteParser.Parse(yaml);

        Assert.Equal("ei/get_events", endpoints[0].Path);
    }

    [Fact]
    public void OmittedTypesAreNullButMockResponseFallsBackToAuthenticatedMessage()
    {
        const string yaml = """
            routes:
              - path: ei/unknown_endpoint
            """;

        var endpoints = RouteParser.Parse(yaml);

        Assert.Null(endpoints[0].Request);
        Assert.Null(endpoints[0].Response);
        // The mock still needs a concrete type to serialize an (empty) response.
        Assert.Equal("AuthenticatedMessage", endpoints[0].MockResponseType);
    }

    [Theory]
    [InlineData("ei/first_contact_secure", "EiFirstContactSecureController")]
    [InlineData("ei_afx/launch_mission", "EiAfxLaunchMissionController")]
    [InlineData("ei_ctx/get_leaderboard", "EiCtxGetLeaderboardController")]
    [InlineData("ei_data", "EiDataController")]
    public void DerivedClassNameFromPath(string path, string expected)
    {
        Assert.Equal(expected, RouteParser.ToClassName(path));
    }
}
