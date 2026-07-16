using System.IO.Compression;
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
       
        Assert.DoesNotContain("UNSIGNED", authStage.Note);
       
        var wrapped = Convert.FromBase64String(authStage.Base64!);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(wrapped);
        Assert.False(string.IsNullOrEmpty(msg.Code));
        Assert.Equal(inner, msg.Message.ToByteArray());
    }

    [Fact]
    public void Build_WrappedWithSalt_CodeMatchesSeederAlgorithm()
    {
       
       
       
        const string salt = "parity-salt";
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = Build(salt).Build(inner, wrap: true);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));

        Assert.Equal(ExpectedCode(inner, salt), msg.Code);
    }

    [Fact]
    public void Build_WithPerRequestSalt_MatchesInstanceSaltResult()
    {
       
       
        const string salt = "per-request-salt";
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var viaInstance = Build(salt).Build(inner, wrap: true);
        var viaPerRequest = Build(null).Build(inner, wrap: true, salt: salt);

        Assert.Equal(viaInstance.FinalBase64, viaPerRequest.FinalBase64);
    }

    [Fact]
    public void Build_WithPerRequestSalt_EmptyMeansUnsigned()
    {
        var inner = new Ei.ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();
       
        var viaInstance = Build(null).Build(inner, wrap: true);
        var viaPerRequest = Build(null).Build(inner, wrap: true, salt: null);
        Assert.Equal(viaInstance.FinalBase64, viaPerRequest.FinalBase64);
    }

    [Fact]
    public void Build_WrappedEmptyMessageWithSalt_DoesNotThrow()
    {
       
       
       
        var empty = new Ei.ContractsInfoRequest().ToByteArray();
        Assert.Empty(empty);

        var result = Build("any-salt").Build(empty, wrap: true);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));

       
        Assert.False(string.IsNullOrEmpty(msg.Code));
    }

   
   
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

    [Fact]
    public void Decode_WrappedCompressedResponse_InflatesThenParses()
    {
        var pipe = Build();
        var response = new Ei.ContractsInfoResponse { ServerTime = 42.0 };
        byte[] gz;
        using (var ms = new MemoryStream())
        {
            using (var z = new GZipStream(ms, CompressionMode.Compress))
                z.Write(response.ToByteArray());
            gz = ms.ToArray();
        }
        var wrapped = new Ei.AuthenticatedMessage
        {
            Message = ByteString.CopyFrom(gz),
            Compressed = true,
        };
        var b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, Ei.ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "inflate");
        Assert.Contains("42", result.Json);
    }

    [Fact]
    public void Decode_UnwrappedResponseResemblingEnvelope_PrefersDirectParse()
    {
       
       
       
       
        var pipe = Build();
        var response = new Ei.ContractsInfoResponse
        {
            ServerTime = 1.5,
           
           
            Contracts = { new Ei.Contract { Identifier = "" } },
        };
        var b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, Ei.ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.Contains("serverTime", result.Json);
    }

    [Fact]
    public void Decode_ResponseWrappedFalse_ForcesDirectEvenWhenBytesResembleEnvelope()
    {
       
       
       
       
        var pipe = Build();
        var response = new Ei.EggIncFirstContactResponse
        {
            EiUserId = "oBlazin",
            Backup = new Ei.Backup { UserName = "player" },
        };
        var b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, Ei.EggIncFirstContactResponse.Parser, responseWrapped: false);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.Contains("oBlazin", result.Json);
    }

    [Fact]
    public void Decode_ResponseWrappedTrue_ForcesWrappedEvenIfHeuristicWouldReject()
    {
       
       
        var pipe = Build();
        var inner = new Ei.ContractsInfoResponse { ServerTime = 7.0 };
        var wrapped = new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(inner.ToByteArray()) };
        var b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, Ei.ContractsInfoResponse.Parser, responseWrapped: true);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains("7", result.Json);
    }

    [Fact]
    public void Decode_WrappedPath_SwallowsOnlyProtoAndDataExceptions()
    {
       
       
       
        var pipe = Build();
        var wrapped = new Ei.AuthenticatedMessage
        {
            Message = ByteString.CopyFrom(new Ei.ContractsInfoResponse { ServerTime = 1.0 }.ToByteArray()),
        };
        var b64 = Convert.ToBase64String(wrapped.ToByteArray());
        var throwingParser = new MessageParser<Ei.ContractsInfoResponse>(
            () => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => pipe.Decode(b64, throwingParser));
        Assert.Equal("boom", ex.Message);
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

    [Fact]
    public void Find_RepeatedLookups_ReturnSameCachedInstances()
    {
        var reflection = new ProtoReflection();
        var parser = reflection.FindParser("ContractsInfoRequest");
        var descriptor = reflection.FindMessage("ContractsInfoRequest");
        Assert.NotNull(parser);
        Assert.NotNull(descriptor);

       
       
        Assert.Same(parser, reflection.FindParser("ContractsInfoRequest"));
        Assert.Same(parser, reflection.FindParser("Ei.ContractsInfoRequest"));
        Assert.Same(descriptor, new ProtoReflection().FindMessage("ContractsInfoRequest"));
    }

    [Fact]
    public void FindParser_UnknownType_StaysNullOnRepeatedProbes()
    {
        var reflection = new ProtoReflection();
        Assert.Null(reflection.FindParser("StillNotARealType"));
        Assert.Null(reflection.FindParser("StillNotARealType"));
        Assert.Null(reflection.FindMessage("StillNotARealType"));
    }
}
