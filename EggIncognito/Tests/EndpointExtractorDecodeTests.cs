using EggIncognito.Core.Services;
using Ei;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class EndpointExtractorDecodeTests {
    private static ContractsInfoRequest SampleRequest() => new() {
        Rinfo = new BasicRequestInfo { EiUserId = "EI1234", ClientVersion = 71 },
        ClientVersion = 71,
        ContractIdentifiers = { "spring-2025", "winter-2024" }
    };

    [Fact]
    public void DecodeRequestBody_RawKnownType_DecodesToInnerType() {
        byte[]? bytes = SampleRequest().ToByteArray();
        Assert.NotNull(ProtoFraming.TryUnwrap(bytes));

        (string? json, string? type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", false, bytes);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
        Assert.Contains("winter-2024", json);
    }

    [Fact]
    public void DecodeRequestBody_WrappedKnownType_UnwrapsThenDecodes_NoMojibake() {
        byte[]? inner = SampleRequest().ToByteArray();
        byte[]? wrapped = new AuthenticatedMessage { Message = ByteString.CopyFrom(inner) }.ToByteArray();

        (string? json, string? type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", true, wrapped);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
        Assert.Contains("clientVersion", json);
    }

    [Fact]
    public void DecodeRequestBody_WrongFraming_StillRecovers_ByTryingBoth() {
        byte[]? inner = SampleRequest().ToByteArray();
        byte[]? wrapped = new AuthenticatedMessage { Message = ByteString.CopyFrom(inner) }.ToByteArray();

        (string? json, string? type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", false, wrapped);

        Assert.Equal("ContractsInfoRequest", type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
    }

    [Fact]
    public void DecodeRequestBody_UnknownType_AutoDetectsInnerType() {
        byte[]? bytes = SampleRequest().ToByteArray();

        (string? json, string? type) = EndpointExtractor.DecodeRequestBody(null, false, bytes);

        Assert.NotNull(type);
        Assert.NotNull(json);
        Assert.Contains("spring-2025", json);
    }

    [Fact]
    public void DecodeRequestBody_Garbage_ReturnsNulls() {
        (string? json, string? type) = EndpointExtractor.DecodeRequestBody("ContractsInfoRequest", false,
            [0xff, 0xff, 0xff, 0xff]);

        Assert.Null(type);
        Assert.Null(json);
    }
}
