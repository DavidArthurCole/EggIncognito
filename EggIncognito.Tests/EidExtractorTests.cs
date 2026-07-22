using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class EidExtractorTests {
    [Fact]
    public void NullData_ReturnsNull() => Assert.Null(EidExtractor.FromData(null));

    [Fact]
    public void Garbage_ReturnsNull() => Assert.Null(EidExtractor.FromData("not-base64!!"));

    [Fact]
    public void WrappedWithUserId_ReturnsId() {
        var msg = new Ei.AuthenticatedMessage { UserId = "EI42", Message = ByteString.Empty };
        var b64 = Convert.ToBase64String(msg.ToByteArray());
        Assert.Equal("EI42", EidExtractor.FromData(b64));
    }

    [Fact]
    public void WrappedNoUserId_ReturnsNull() {
        var msg = new Ei.AuthenticatedMessage { Message = ByteString.Empty };
        Assert.Null(EidExtractor.FromData(Convert.ToBase64String(msg.ToByteArray())));
    }
}
