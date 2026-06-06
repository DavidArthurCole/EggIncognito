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
    public void Build_Unwrapped_HasFourStagesAndSkipsAuthMessage()
    {
        var pipe = Build();
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = pipe.Build(inner, wrap: false);

        Assert.Collection(result.Stages,
            s => Assert.Equal("proto-encode", s.Name),
            s => { Assert.Equal("authenticated-message", s.Name); Assert.Contains("skipped", s.Note); },
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
        Assert.Null(authStage.Note); // signed, no warning
        // The wrapped bytes must parse back into an AuthenticatedMessage with a non-empty code.
        var wrapped = Convert.FromBase64String(authStage.Base64!);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(wrapped);
        Assert.False(string.IsNullOrEmpty(msg.Code));
        Assert.Equal(inner, msg.Message.ToByteArray());
    }

    [Fact]
    public void Build_WrappedWithoutSalt_FlagsUnsigned()
    {
        var pipe = Build();
        Assert.False(pipe.CanSign);
        var inner = new Ei.ContractsInfoRequest().ToByteArray();

        var result = pipe.Build(inner, wrap: true);

        var authStage = result.Stages.Single(s => s.Name == "authenticated-message");
        Assert.Contains("unsigned", authStage.Note);
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

public class EndpointCatalogTests
{
    private const string Yaml = """
endpoints:
  - path: ei/first_contact_secure
    requestType: EggIncFirstContactRequest
    responseType: EggIncFirstContactResponse
  - path: ei_ctx/get_contracts_info
    requestType: ContractsInfoRequest
    responseType: ContractsInfoResponse
  - path: ei/process_shells_actions
    requestType: ShellsActionBatch
    rawResponse: "OK"
  - path: ei_ctx/get_contract_evaluation
    requestType: AuthenticatedMessage
    responseType: ContractEvaluation
    pathParam: true

excluded:
  - ei/kb

fixture_status:
  empty:
    - ei/clean_accounts
""";

    private static EndpointCatalog Build()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, Yaml);
        return new EndpointCatalog(path);
    }

    [Fact]
    public void Parse_ReadsOnlyEndpointsSection()
    {
        var cat = Build();
        // ei/kb (excluded) and ei/clean_accounts (fixture_status) must NOT be endpoints.
        Assert.Null(cat.Get("ei/kb"));
        Assert.Equal(4, cat.All().Count);
    }

    [Fact]
    public void Parse_NewContractsInfoEndpoint_HasCorrectTypes()
    {
        var e = Build().Get("ei_ctx/get_contracts_info");
        Assert.NotNull(e);
        Assert.Equal("ContractsInfoRequest", e!.RequestType);
        Assert.Equal("ContractsInfoResponse", e.ResponseType);
        Assert.False(e.Wrap);
    }

    [Fact]
    public void Parse_FirstContact_IsMarkedWrap()
    {
        Assert.True(Build().Get("ei/first_contact_secure")!.Wrap);
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
