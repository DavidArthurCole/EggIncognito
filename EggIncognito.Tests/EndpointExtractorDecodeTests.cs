using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

// Guards the request-body framing heuristic in EndpointExtractor.DecodeRequestBody - the shared
// decode used by BOTH the live capture pipeline and the dashboard decoder. The regression it
// protects against is "mojibake": parsing an AM-wrapped blob as if it were raw (or vice versa)
// makes the lenient proto parser stuff the real payload into one giant trailing string field
// instead of decoding the true inner type.
public class EndpointExtractorDecodeTests
{
    // A request type with several distinct fields, so a correct decode has many ':' (fields) while
    // a mojibake decode collapses to one.
    private static Ei.ContractsInfoRequest SampleRequest() => new()
    {
        Rinfo = new Ei.BasicRequestInfo { EiUserId = "EI1234", ClientVersion = 71 },
        ClientVersion = 71,
        ContractIdentifiers = { "spring-2025", "winter-2024" },
    };

    [Fact]
    public void DecodeRequestBody_RawKnownType_DecodesToInnerType()
    {
        var bytes = SampleRequest().ToByteArray();
        // These raw bytes are coincidentally also AM-unwrappable into garbage, so the decode tries
        // two candidates. The bad one must not discard the good (raw) decode - the guarded loop.
        Assert.NotNull(EndpointExtractor.TryUnwrap(bytes));

        var (json, type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", wrapped: false, bytes);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
        Assert.Contains("winter-2024", json);
    }

    [Fact]
    public void DecodeRequestBody_WrappedKnownType_UnwrapsThenDecodes_NoMojibake()
    {
        var inner = SampleRequest().ToByteArray();
        var wrapped = new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(inner) }.ToByteArray();

        var (json, type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", wrapped: true, wrapped);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        // The true inner fields survive - not swallowed into a single mojibake string.
        Assert.Contains("spring-2025", json);
        Assert.Contains("clientVersion", json);
    }

    [Fact]
    public void DecodeRequestBody_WrongFraming_StillRecovers_ByTryingBoth()
    {
        // Caller claims raw, but the bytes are actually AM-wrapped. The heuristic tries both
        // framings and keeps the one that round-trips / has the richest fields.
        var inner = SampleRequest().ToByteArray();
        var wrapped = new Ei.AuthenticatedMessage { Message = ByteString.CopyFrom(inner) }.ToByteArray();

        var (json, type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", wrapped: false, wrapped);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
    }

    [Fact]
    public void DecodeRequestBody_UnknownType_AutoDetectsInnerType()
    {
        var bytes = SampleRequest().ToByteArray();

        var (json, type) = EndpointExtractor.DecodeRequestBody(knownType: null, wrapped: false, bytes);

        Assert.NotNull(type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
    }

    [Fact]
    public void DecodeRequestBody_Garbage_ReturnsNulls()
    {
        var (json, type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", wrapped: false,
            new byte[] { 0xff, 0xff, 0xff, 0xff });

        // A wrong-type parse of random bytes yields no usable decode.
        Assert.Null(type);
        Assert.Null(json);
    }
}
