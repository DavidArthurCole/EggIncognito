// EggIncognito.Tests/TransportPipelineTests.cs
using Google.Protobuf;
using Microsoft.Extensions.Configuration;
using EggIncognito.Services;

namespace EggIncognito.Tests;

public class TransportPipelineTests
{
    private static TransportPipeline Build(string? salt = null)
    {
        var dict = new Dictionary<string, string?>();
        if (salt is not null) dict["EGG_INC_API_SALT"] = salt;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new TransportPipeline(config);
    }

    [Fact]
    public void Build_Unwrapped_PassesThroughWithRealBytes()
    {
        var pipe = Build();
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = pipe.Build(inner, wrap: false);

        Assert.Collection(result.Stages,
            s => Assert.Equal("proto-encode", s.Name),
            s =>
            {
                // No empty "skipped" card: the request IS the proto bytes, posted as-is.
                Assert.Equal("passthrough", s.Name);
                Assert.Equal("payload", s.Role);
                Assert.False(s.Skipped);
                Assert.Equal(inner.Length, s.ByteLength);
                Assert.Equal(Convert.ToBase64String(inner), s.Base64);
            },
            s => Assert.Equal("base64", s.Name),
            s => Assert.Equal("form-urlencode", s.Name));
        Assert.Equal(Convert.ToBase64String(inner), result.FinalBase64);
        Assert.StartsWith("data=", result.FinalFormBody);
    }

    [Fact]
    public void Build_WrappedWithSalt_ProducesSignedAuthMessage()
    {
        var pipe = Build("test-salt");
        Assert.True(pipe.CanSign);
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = pipe.Build(inner, wrap: true);

        var authStage = result.Stages.Single(s => s.Name == "authenticated-message");
        Assert.Equal("envelope", authStage.Role);
        // Signed: the note is the envelope explainer, not an unsigned warning.
        Assert.DoesNotContain("UNSIGNED", authStage.Note);
        // The wrapped bytes must parse back into an AuthenticatedMessage with a non-empty code.
        var wrapped = Convert.FromBase64String(authStage.Base64!);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(wrapped);
        Assert.False(string.IsNullOrEmpty(msg.Code));
        Assert.Equal(inner, msg.Message.ToByteArray());
    }

    [Fact]
    public void Build_WrappedWithSalt_CodeMatchesSeederAlgorithm()
    {
        // Parity lock: the AuthenticatedMessage code TransportPipeline produces must equal the
        // SHA256(mutated-message + hex(SHA256(salt))) algorithm the Seeder used before its copy was
        // deleted. Guards the dedup (Seeder now calls TransportPipeline) from silent drift.
        const string salt = "parity-salt";
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = Build(salt).Build(inner, wrap: true);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));

        Assert.Equal(ExpectedCode(inner, salt), msg.Code);
    }

    // Independent reimplementation of the documented signing algorithm (NOT a call into the code
    // under test) so this test fails if the production algorithm changes.
    private static string ExpectedCode(byte[] messageBytes, string phrase)
    {
        var phraseHash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(phrase));
        var saltBytes = System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(phraseHash).ToLowerInvariant());
        const uint magic = 0x3b9af419;
        var mutated = (byte[])messageBytes.Clone();
        mutated[magic % (uint)mutated.Length] = 0x1b;
        var combined = new byte[mutated.Length + saltBytes.Length];
        mutated.CopyTo(combined, 0);
        saltBytes.CopyTo(combined, mutated.Length);
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(combined)).ToLowerInvariant();
    }

    [Fact]
    public void Build_WrappedWithoutSalt_FlagsUnsigned()
    {
        var pipe = Build();
        Assert.False(pipe.CanSign);
        var inner = new Ei.ContractsInfoRequest().ToByteArray();

        var result = pipe.Build(inner, wrap: true);

        var authStage = result.Stages.Single(s => s.Name == "authenticated-message");
        Assert.Contains("UNSIGNED", authStage.Note);
    }

    [Fact]
    public void Decode_UnwrappedMockResponse_ParsesDirectly()
    {
        var pipe = Build();
        var response = new Ei.ContractsInfoResponse
        {
            ServerTime = 123.0,
            Contracts = { new Ei.Contract { Identifier = "spring-2025" } },
        };
        var b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, Ei.ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains("spring-2025", result.Json);
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
    }

    [Fact]
    public void Decode_WrappedRealResponse_UnwrapsThenParses()
    {
        var pipe = Build();
        var response = new Ei.ContractsInfoResponse { ServerTime = 99.0 };
        var wrapped = new Ei.AuthenticatedMessage
        {
            Message = ByteString.CopyFrom(response.ToByteArray()),
        };
        var b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, Ei.ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains("99", result.Json);
    }
}

public class RouteCatalogTests
{
    private const string Yaml = """
routes:
  - path: ei/first_contact_secure
    request: EggIncFirstContactRequest
    requestWrapped: true
    response: EggIncFirstContactResponse
    responseWrapped: true
  - path: ei_ctx/get_contracts_info
    request: ContractsInfoRequest
    response: ContractsInfoResponse
  - path: ei/process_shells_actions
    request: ShellsActionBatch
    rawResponse: "OK"
  - path: ei_ctx/get_contract_evaluation
    requestType: AuthenticatedMessage
    response: ContractEvaluation
    pathParam: true
  - path: ei_srv/subscription_status
    response: UserSubscriptionInfo
    responseWrapped: true
    pathParam: true
    pathParamOnly: true

excluded:
  - ei/kb

endpoint_status:
  empty:
    - ei/clean_accounts
""";

    private static RouteCatalog Build()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, Yaml);
        return new RouteCatalog(path);
    }

    [Fact]
    public void Parse_ReadsOnlyEndpointsSection()
    {
        var cat = Build();
        // ei/kb (excluded) and ei/clean_accounts (endpoint_status) must NOT be endpoints.
        Assert.Null(cat.Get("ei/kb"));
        Assert.Equal(5, cat.All().Count);
    }

    [Fact]
    public void Parse_NewContractsInfoEndpoint_HasCorrectTypes()
    {
        var e = Build().Get("ei_ctx/get_contracts_info");
        Assert.NotNull(e);
        Assert.Equal("ContractsInfoRequest", e!.Request);
        Assert.Equal("ContractsInfoResponse", e.Response);
        Assert.False(e.RequestWrapped);
        Assert.False(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_FirstContact_IsMarkedRequestWrapped()
    {
        var e = Build().Get("ei/first_contact_secure")!;
        Assert.True(e.RequestWrapped);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_SubscriptionStatus_IsPathParamOnlyWithKnownResponse()
    {
        var e = Build().Get("ei_srv/subscription_status")!;
        Assert.True(e.PathParamOnly);
        Assert.True(e.PathParam);
        Assert.Null(e.Request);
        Assert.Equal("UserSubscriptionInfo", e.Response);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_LegacyAuthenticatedMessageRequest_NormalizesToWrapped()
    {
        // ei_ctx/get_contract_evaluation still uses legacy requestType: AuthenticatedMessage.
        var e = Build().Get("ei_ctx/get_contract_evaluation")!;
        Assert.Null(e.Request);
        Assert.True(e.RequestWrapped);
        Assert.Equal("ContractEvaluation", e.Response);
    }

    [Fact]
    public void Parse_PathParamAndRawResponse_Captured()
    {
        var cat = Build();
        Assert.True(cat.Get("ei_ctx/get_contract_evaluation")!.PathParam);
        Assert.Equal("OK", cat.Get("ei/process_shells_actions")!.RawResponse);
    }
}

public class ProtoReflectionTests
{
    [Fact]
    public void Schema_ContractsInfoRequest_ListsFields()
    {
        var schema = new ProtoReflection().Schema("ContractsInfoRequest");
        Assert.NotNull(schema);
        var byName = schema!.Fields.ToDictionary(f => f.Name);
        Assert.Equal("message", byName["rinfo"].Type);
        Assert.Equal("BasicRequestInfo", byName["rinfo"].MessageType);
        Assert.True(byName["contract_identifiers"].Repeated);
        Assert.Equal("string", byName["contract_identifiers"].Type);
        Assert.Equal("uint32", byName["client_version"].Type);
    }

    [Fact]
    public void FindParser_UnknownType_ReturnsNull()
    {
        Assert.Null(new ProtoReflection().FindParser("NotARealType"));
    }
}
