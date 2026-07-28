using EggIncognito.RouteGenerator;

namespace EggIncognito.Tests;

public class GeneratorTests {
    [Fact]
    public void ParsesEndpointsFromYaml() {
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
    public void NewSchemaKeysWinAndModelTheThreeAxes() {
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


        Assert.Null(e[1].Request);
        Assert.True(e[1].PathParamOnly);
        Assert.Equal("UserSubscriptionInfo", e[1].Response);
        Assert.True(e[1].ResponseWrapped);

        Assert.Equal("UserSubscriptionInfo", e[1].MockResponseType);
    }

    [Fact]
    public void InlineCommentsAreStrippedFromValues() {
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


        Assert.Equal("SomeRequest", e[0].Request);
        Assert.Null(e[0].Response);
        Assert.Equal("AuthenticatedMessage", e[0].MockResponseType);
        Assert.True(e[0].ResponseWrapped);

        Assert.Null(e[1].Request);
        Assert.True(e[1].RequestWrapped);
        Assert.Equal("KnownResponse", e[1].Response);
    }

    [Fact]
    public void LegacyAuthenticatedMessageNormalizesToWrappedUnknown() {
        const string yaml = """
                            routes:
                              - path: ei/get_events
                                requestType: AuthenticatedMessage
                                responseType: EggIncCurrentEvents
                            """;

        var e = RouteParser.Parse(yaml)[0];


        Assert.Null(e.Request);
        Assert.True(e.RequestWrapped);
        Assert.Equal("EggIncCurrentEvents", e.Response);
        Assert.False(e.ResponseWrapped);
    }

    [Fact]
    public void StripsTrailingSlashFromPath() {
        const string yaml = """
                            routes:
                              - path: ei/get_events/
                                responseType: EggIncCurrentEvents
                            """;

        var endpoints = RouteParser.Parse(yaml);

        Assert.Equal("ei/get_events", endpoints[0].Path);
    }

    [Fact]
    public void OmittedTypesAreNullButMockResponseFallsBackToAuthenticatedMessage() {
        const string yaml = """
                            routes:
                              - path: ei/unknown_endpoint
                            """;

        var endpoints = RouteParser.Parse(yaml);

        Assert.Null(endpoints[0].Request);
        Assert.Null(endpoints[0].Response);

        Assert.Equal("AuthenticatedMessage", endpoints[0].MockResponseType);
    }

    [Fact]
    public void ParseOutput_HasValueEquality_ForIdenticalYaml() {
        const string yaml = """
                            routes:
                              - path: ei/first_contact_secure
                                request: EggIncFirstContactRequest
                                requestWrapped: true
                                response: EggIncFirstContactResponse
                                responseWrapped: true
                              - path: ei/process_shells_actions
                                requestType: ShellsActionBatch
                                rawResponse: "OK"
                              - path: ei_srv/subscription_status
                                response: UserSubscriptionInfo
                                pathParam: true
                                pathParamOnly: true
                            """;

        var a = RouteParser.Parse(yaml);
        var b = RouteParser.Parse(yaml);

        Assert.Equal(a, b);
        Assert.True(RouteListComparer.Instance.Equals(a, b));
        Assert.Equal(RouteListComparer.Instance.GetHashCode(a), RouteListComparer.Instance.GetHashCode(b));
    }

    [Fact]
    public void ParseOutput_Differs_WhenARouteChanges() {
        const string yaml = """
                            routes:
                              - path: ei/get_events
                                response: EggIncCurrentEvents
                            """;
        var a = RouteParser.Parse(yaml);
        var b = RouteParser.Parse(yaml.Replace("EggIncCurrentEvents", "SomethingElse"));

        Assert.False(RouteListComparer.Instance.Equals(a, b));
    }

    [Theory]
    [InlineData("ei/first_contact_secure", "EiFirstContactSecureController")]
    [InlineData("ei_afx/launch_mission", "EiAfxLaunchMissionController")]
    [InlineData("ei_ctx/get_leaderboard", "EiCtxGetLeaderboardController")]
    [InlineData("ei_data", "EiDataController")]
    public void DerivedClassNameFromPath(string path, string expected) =>
        Assert.Equal(expected, RouteParser.ToClassName(path));
}
