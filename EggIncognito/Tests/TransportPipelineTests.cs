using System.IO.Compression;
using System.Text;
using EggIncognito.Core;
using EggIncognito.Services;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Tests;

public class TransportPipelineTests {
    private static TransportPipeline Build(string? salt = null) {
        var dict = new Dictionary<string, string?>();
        if (salt is not null) dict["EGG_INC_API_SALT"] = salt;
        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        return new TransportPipeline(config);
    }

    [Fact]
    public void Build_Unwrapped_PassesThroughWithRealBytes() {
        var pipe = Build();
        byte[]? inner = new ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = pipe.Build(inner, false);

        Assert.Collection(result.Stages,
            s => Assert.Equal("proto-encode", s.Name),
            s => {
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
    public void Build_WrappedWithSalt_ProducesSignedAuthMessage() {
        var pipe = Build("test-salt");
        Assert.True(pipe.CanSign);
        byte[]? inner = new ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = pipe.Build(inner, true);

        var authStage = result.Stages.Single(s => s.Name == "authenticated-message");
        Assert.Equal("envelope", authStage.Role);

        Assert.DoesNotContain("UNSIGNED", authStage.Note);

        byte[] wrapped = Convert.FromBase64String(authStage.Base64!);
        var msg = AuthenticatedMessage.Parser.ParseFrom(wrapped);
        Assert.False(string.IsNullOrEmpty(msg.Code));
        Assert.Equal(inner, msg.Message.ToByteArray());
    }

    [Fact]
    public void Build_WrappedWithSalt_CodeMatchesSeederAlgorithm() {
        const string salt = "parity-salt";
        byte[]? inner = new ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var result = Build(salt).Build(inner, true);
        var msg = AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));

        Assert.Equal(ExpectedCode(inner, salt), msg.Code);
    }

    [Fact]
    public void Build_WithPerRequestSalt_MatchesInstanceSaltResult() {
        const string salt = "per-request-salt";
        byte[]? inner = new ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var viaInstance = Build(salt).Build(inner, true);
        var viaPerRequest = Build().Build(inner, true, salt);

        Assert.Equal(viaInstance.FinalBase64, viaPerRequest.FinalBase64);
    }

    [Fact]
    public void Build_WithPerRequestSalt_EmptyMeansUnsigned() {
        byte[]? inner = new ContractsInfoRequest { ClientVersion = 71 }.ToByteArray();

        var viaInstance = Build().Build(inner, true);
        var viaPerRequest = Build().Build(inner, true, null);
        Assert.Equal(viaInstance.FinalBase64, viaPerRequest.FinalBase64);
    }

    [Fact]
    public void Build_WrappedEmptyMessageWithSalt_DoesNotThrow() {
        byte[]? empty = new ContractsInfoRequest().ToByteArray();
        Assert.Empty(empty);

        var result = Build("any-salt").Build(empty, true);
        var msg = AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));


        Assert.False(string.IsNullOrEmpty(msg.Code));
    }


    private static string ExpectedCode(byte[] messageBytes, string phrase) {
        byte[] saltBytes = Encoding.ASCII.GetBytes(Hashes.Sha256Hex(phrase));
        const uint magic = 0x3b9af419;
        byte[] mutated = (byte[])messageBytes.Clone();
        mutated[magic % (uint)mutated.Length] = 0x1b;
        byte[] combined = new byte[mutated.Length + saltBytes.Length];
        mutated.CopyTo(combined, 0);
        saltBytes.CopyTo(combined, mutated.Length);
        return Hashes.Sha256Hex(combined);
    }

    [Fact]
    public void Build_WrappedWithoutSalt_FlagsUnsigned() {
        var pipe = Build();
        Assert.False(pipe.CanSign);
        byte[]? inner = new ContractsInfoRequest().ToByteArray();

        var result = pipe.Build(inner, true);

        var authStage = result.Stages.Single(s => s.Name == "authenticated-message");
        Assert.Contains("UNSIGNED", authStage.Note);
    }

    [Fact]
    public void Decode_UnwrappedMockResponse_ParsesDirectly() {
        var pipe = Build();
        var response = new ContractsInfoResponse {
            ServerTime = 123.0,
            Contracts = { new Contract { Identifier = "spring-2025" } }
        };
        string b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains("spring-2025", result.Json);
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
    }

    [Fact]
    public void Decode_WrappedRealResponse_UnwrapsThenParses() {
        var pipe = Build();
        var response = new ContractsInfoResponse { ServerTime = 99.0 };
        var wrapped = new AuthenticatedMessage {
            Message = ByteString.CopyFrom(response.ToByteArray())
        };
        string b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains("99", result.Json);
    }

    [Fact]
    public void Decode_WrappedCompressedResponse_InflatesThenParses() {
        var pipe = Build();
        var response = new ContractsInfoResponse { ServerTime = 42.0 };
        byte[] gz;
        using (var ms = new MemoryStream()) {
            using (var z = new GZipStream(ms, CompressionMode.Compress))
                z.Write(response.ToByteArray());
            gz = ms.ToArray();
        }

        var wrapped = new AuthenticatedMessage {
            Message = ByteString.CopyFrom(gz),
            Compressed = true
        };
        string b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "inflate");
        Assert.Contains("42", result.Json);
    }

    [Fact]
    public void Decode_UnwrappedResponseResemblingEnvelope_PrefersDirectParse() {
        var pipe = Build();
        var response = new ContractsInfoResponse {
            ServerTime = 1.5,


            Contracts = { new Contract { Identifier = "" } }
        };
        string b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, ContractsInfoResponse.Parser);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.Contains("serverTime", result.Json);
    }

    [Fact]
    public void Decode_ResponseWrappedFalse_ForcesDirectEvenWhenBytesResembleEnvelope() {
        var pipe = Build();
        var response = new EggIncFirstContactResponse {
            EiUserId = "oBlazin",
            Backup = new Backup { UserName = "player" }
        };
        string b64 = Convert.ToBase64String(response.ToByteArray());

        var result = pipe.Decode(b64, EggIncFirstContactResponse.Parser, false);

        Assert.Null(result.Error);
        Assert.DoesNotContain(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains(result.Stages, s => s.Name == "proto-decode");
        Assert.Contains("oBlazin", result.Json);
    }

    [Fact]
    public void Decode_ResponseWrappedTrue_ForcesWrappedEvenIfHeuristicWouldReject() {
        var pipe = Build();
        var inner = new ContractsInfoResponse { ServerTime = 7.0 };
        var wrapped = new AuthenticatedMessage { Message = ByteString.CopyFrom(inner.ToByteArray()) };
        string b64 = Convert.ToBase64String(wrapped.ToByteArray());

        var result = pipe.Decode(b64, ContractsInfoResponse.Parser, true);

        Assert.Null(result.Error);
        Assert.Contains(result.Stages, s => s.Name == "authenticated-message");
        Assert.Contains("7", result.Json);
    }

    [Fact]
    public void Decode_WrappedPath_SwallowsOnlyProtoAndDataExceptions() {
        var pipe = Build();
        var wrapped = new AuthenticatedMessage {
            Message = ByteString.CopyFrom(new ContractsInfoResponse { ServerTime = 1.0 }.ToByteArray())
        };
        string b64 = Convert.ToBase64String(wrapped.ToByteArray());
        var throwingParser =
            new MessageParser<ContractsInfoResponse>(() => throw new InvalidOperationException("boom"));

        var ex = Assert.Throws<InvalidOperationException>(() => pipe.Decode(b64, throwingParser));
        Assert.Equal("boom", ex.Message);
    }
}

public class RouteCatalogTests {
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
                                  - path: ei/coop_status
                                    requestType: ContractCoopStatusRequest
                                    responseType: ContractCoopStatusResponse
                                    responseWrapped: true

                                excluded:
                                  - ei/kb

                                endpoint_status:
                                  empty:
                                    - ei/clean_accounts
                                """;

    private static RouteCatalog Build() {
        string path = Path.GetTempFileName();
        File.WriteAllText(path, Yaml);
        return new RouteCatalog(path);
    }

    [Fact]
    public void Parse_ReadsOnlyEndpointsSection() {
        var cat = Build();

        Assert.Null(cat.Get("ei/kb"));
        Assert.Equal(6, cat.All().Count);
    }

    [Fact]
    public void Parse_LegacyTypeKeys_HonorExplicitResponseWrapped() {
        var e = Build().Get("ei/coop_status")!;
        Assert.Equal("ContractCoopStatusRequest", e.Request);
        Assert.Equal("ContractCoopStatusResponse", e.Response);
        Assert.False(e.RequestWrapped);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_NewContractsInfoEndpoint_HasCorrectTypes() {
        var e = Build().Get("ei_ctx/get_contracts_info");
        Assert.NotNull(e);
        Assert.Equal("ContractsInfoRequest", e.Request);
        Assert.Equal("ContractsInfoResponse", e.Response);
        Assert.False(e.RequestWrapped);
        Assert.False(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_FirstContact_IsMarkedRequestWrapped() {
        var e = Build().Get("ei/first_contact_secure")!;
        Assert.True(e.RequestWrapped);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_SubscriptionStatus_IsPathParamOnlyWithKnownResponse() {
        var e = Build().Get("ei_srv/subscription_status")!;
        Assert.True(e.PathParamOnly);
        Assert.True(e.PathParam);
        Assert.Null(e.Request);
        Assert.Equal("UserSubscriptionInfo", e.Response);
        Assert.True(e.ResponseWrapped);
    }

    [Fact]
    public void Parse_LegacyAuthenticatedMessageRequest_NormalizesToWrapped() {
        var e = Build().Get("ei_ctx/get_contract_evaluation")!;
        Assert.Null(e.Request);
        Assert.True(e.RequestWrapped);
        Assert.Equal("ContractEvaluation", e.Response);
    }

    [Fact]
    public void Parse_PathParamAndRawResponse_Captured() {
        var cat = Build();
        Assert.True(cat.Get("ei_ctx/get_contract_evaluation")!.PathParam);
        Assert.Equal("OK", cat.Get("ei/process_shells_actions")!.RawResponse);
    }
}

public class ProtoReflectionTests {
    [Fact]
    public void Schema_ContractsInfoRequest_ListsFields() {
        var schema = new ProtoReflection().Schema("ContractsInfoRequest");
        Assert.NotNull(schema);
        var byName = schema.Fields.ToDictionary(f => f.Name);
        Assert.Equal("message", byName["rinfo"].Type);
        Assert.Equal("BasicRequestInfo", byName["rinfo"].MessageType);
        Assert.True(byName["contract_identifiers"].Repeated);
        Assert.Equal("string", byName["contract_identifiers"].Type);
        Assert.Equal("uint32", byName["client_version"].Type);
    }

    [Fact]
    public void FindParser_UnknownType_ReturnsNull() => Assert.Null(new ProtoReflection().FindParser("NotARealType"));

    [Fact]
    public void Find_RepeatedLookups_ReturnSameCachedInstances() {
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
    public void FindParser_UnknownType_StaysNullOnRepeatedProbes() {
        var reflection = new ProtoReflection();
        Assert.Null(reflection.FindParser("StillNotARealType"));
        Assert.Null(reflection.FindParser("StillNotARealType"));
        Assert.Null(reflection.FindMessage("StillNotARealType"));
    }
}
