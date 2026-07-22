using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class BlobDecoderTests {
    [Fact]
    public void Decode_KnownProto_IdentifiesTypeAndJson() {
        var msg = new Ei.ContractsInfoRequest { ClientVersion = 71 };
        var r = BlobDecoder.Decode(Convert.ToBase64String(msg.ToByteArray()));
        Assert.NotNull(r.Type);
        Assert.NotNull(r.Json);
    }

    [Fact]
    public void Decode_Garbage_ReturnsNullType() => Assert.Null(BlobDecoder.Decode("!!!notbase64").Type);
}
