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
        // SHA256(mutated-message + hex(SHA256(salt))) algorithm, pinned so the single signing home
        // (TransportPipeline) cannot silently drift from the wire format the real API expects.
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
        // The per-request-salt overload must produce byte-identical output to constructing the
        // pipeline with that same salt - proving the new path routes through the one signing home.
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
        // No salt anywhere -> the wrap stage is built unsigned (no Code), same as the env-less path.
        var viaInstance = Build(null).Build(inner, wrap: true);
        var viaPerRequest = Build(null).Build(inner, wrap: true, salt: null);
        Assert.Equal(viaInstance.FinalBase64, viaPerRequest.FinalBase64);
    }

    [Fact]
    public void Build_WrappedEmptyMessageWithSalt_DoesNotThrow()
    {
        // An all-default proto serializes to zero bytes. Signing must not divide by the message
        // length (DivideByZeroException) - a 0-byte message has no byte to mutate, so it signs the
        // empty message as-is. The Inspector can send such a request, so this must not 500.
        var empty = new Ei.ContractsInfoRequest().ToByteArray();
        Assert.Empty(empty);

        var result = Build("any-salt").Build(empty, wrap: true);
        var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(
            Convert.FromBase64String(result.Stages.Single(s => s.Name == "authenticated-message").Base64!));

        // Code is present (signed), computed over the empty message + salt without the mutation step.
        Assert.False(string.IsNullOrEmpty(msg.Code));
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
        // An unwrapped response whose field 1 is length-delimited also "parses" as an
        // AuthenticatedMessage (field 1 = message). serverTime is field 4 wire i64 while the
        // envelope's compressed (field 4) is varint, so the envelope-shape gate must route these
        // bytes to the direct path instead of mislabeling them wrapped and dropping serverTime.
        var pipe = Build();
        var response = new Ei.ContractsInfoResponse
        {
            ServerTime = 1.5,
            // The contract entry's bytes (0x0A 0x00) are themselves valid wire shape, so they
            // tolerantly re-parse as the response type and the old wrapped-first path won.
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
        // EggIncFirstContactResponse with only field 1 (backup) + field 2 (ei_user_id), both
        // length-delimited, is byte-indistinguishable from a 2-field AuthenticatedMessage
        // {message, code}, so LooksLikeAuthEnvelope passes. The route declares responseWrapped:false;
        // the decoder must honor that and parse direct rather than mislabel it wrapped.
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
        // A route declaring responseWrapped:true must take the wrapped path unconditionally, even
        // when the inner payload's wire shape would fail LooksLikeAuthEnvelope on its own.
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
        // The wrapped-path probe swallows InvalidProtocolBufferException/InvalidDataException
        // (the expected "not actually wrapped" signals) and falls back to the direct parse.
        // Anything else must propagate instead of being masked as a direct-parse error.
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

    [Fact]
    public void Find_RepeatedLookups_ReturnSameCachedInstances()
    {
        var reflection = new ProtoReflection();
        var parser = reflection.FindParser("ContractsInfoRequest");
        var descriptor = reflection.FindMessage("ContractsInfoRequest");
        Assert.NotNull(parser);
        Assert.NotNull(descriptor);

        // Same instance on repeated calls, also via the "Ei."-prefixed alias and a fresh instance:
        // the cache is keyed by short name and shared.
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
