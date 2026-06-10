using Svc = EggIncognito.Services;

namespace EggIncognito.Tests;

public class RedactorTests
{
    [Theory]
    [InlineData("originalTransactionId")]
    [InlineData("transactionId")]
    [InlineData("linkedTransactionId")]
    [InlineData("deviceId")]
    [InlineData("deviceName")]
    [InlineData("pushUserId")]
    [InlineData("gameServicesId")]
    [InlineData("gameServicesIdScoped")]
    [InlineData("code")]
    [InlineData("coopIdentifier")]
    [InlineData("userName")]
    [InlineData("requestingUserName")]
    [InlineData("alias")]
    public void Redact_JumblesSensitiveField(string field)
    {
        var json = $"{{ \"{field}\": \"super-secret-value-123\" }}";
        var red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("super-secret-value-123", red);
        Assert.Contains($"\"{field}\":", red); // key preserved
        Assert.Contains("redacted-", red); // value tokenized
    }

    [Fact]
    public void Redact_IsStable()
    {
        var json = "{ \"deviceId\": \"abc123\" }";
        Assert.Equal(Svc.Redactor.Redact(json), Svc.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_LeavesNonSensitiveFieldsUntouched()
    {
        var json = "{ \"soulEggs\": \"4659456007327222784\", \"currentEgg\": 14, \"sku\": \"cc_standard\" }";
        Assert.Equal(json, Svc.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_RealisticSubscriptionPayload()
    {
        var json = "{ \"originalTransactionId\": \"amdflgjddogdgeejiamdpaej.AO-J1Oy8\", \"periodEnd\": 1782945704.448 }";
        var red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("amdflgjddogdgeejiamdpaej", red);
        Assert.Contains("1782945704.448", red); // non-sensitive numeric kept
    }
}
