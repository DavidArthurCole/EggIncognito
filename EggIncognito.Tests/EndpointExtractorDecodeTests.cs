using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

// Guards against "mojibake": parsing an AM-wrapped blob as raw (or vice versa) makes the lenient
// proto parser stuff the real payload into one giant trailing string field instead of the true type.
public class EndpointExtractorDecodeTests
{
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
        Assert.NotNull(ProtoFraming.TryUnwrap(bytes));

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
        Assert.Contains("spring-2025", json);
        Assert.Contains("clientVersion", json);
    }

    [Fact]
    public void DecodeRequestBody_WrongFraming_StillRecovers_ByTryingBoth()
    {
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

        Assert.Null(type);
        Assert.Null(json);
    }
}
