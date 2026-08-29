using System.Reflection;
using System.Text.RegularExpressions;
using Ei;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Svc = EggIncognito.Core.Services;

namespace EggIncognito.Tests;

public partial class RedactorTests {
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
    [InlineData("signature")]
    [InlineData("receipt")]
    [InlineData("advertisingId")]
    [InlineData("deviceAdId")]
    [InlineData("pushId")]
    [InlineData("coopIdentifier")]
    [InlineData("userName")]
    [InlineData("requestingUserName")]
    [InlineData("username")]
    [InlineData("alias")]
    public void Redact_JumblesSensitiveField(string field) {
        string json = $"{{ \"{field}\": \"super-secret-value-123\" }}";
        string red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("super-secret-value-123", red);
        Assert.Contains($"\"{field}\":", red);
        Assert.Contains("redacted-", red);
    }

    [Fact]
    public void Redact_IsStable() {
        const string json = "{ \"deviceId\": \"abc123\" }";
        Assert.Equal(Svc.Redactor.Redact(json), Svc.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_LeavesNonSensitiveFieldsUntouched() {
        const string json = "{ \"soulEggs\": \"4659456007327222784\", \"currentEgg\": 14, \"sku\": \"cc_standard\" }";
        Assert.Equal(json, Svc.Redactor.Redact(json));
    }

    [Fact]
    public void Redact_RealisticSubscriptionPayload() {
        const string json =
            "{ \"originalTransactionId\": \"amdflgjddogdgeejiamdpaej.AO-J1Oy8\", \"periodEnd\": 1782945704.448 }";
        string red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("amdflgjddogdgeejiamdpaej", red);
        Assert.Contains("1782945704.448", red);
    }

    [Fact]
    public void Redact_ValueWithEscapedQuote_IsConsumedWhole() {
        const string json = "{ \"deviceName\": \"say \\\" hideout\" }";
        string red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("say", red);
        Assert.DoesNotContain("hideout", red);
        Assert.Contains("redacted-", red);
    }

    [Fact]
    public void Redact_ValueEndingInEscapedBackslash_DoesNotOverrun() {
        const string json = "{ \"deviceId\": \"trail\\\\\", \"keepMe\": \"visible\" }";
        string red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("trail", red);
        Assert.Contains("\"keepMe\": \"visible\"", red);
    }


    [Fact]
    public void SensitiveFields_CoverEveryPiiLookingProtoStringField() {
        var piiShaped = PiiShapedRegex();


        var safe = new HashSet<string>(StringComparer.Ordinal) {
            "userId", "eiUserId", "coopUserId", "requestingUserId", "destUserId",
            "toEiUserId", "eiUserIdToKeep", "pastUserIds", "playerIdentifier",

            "identifier", "contractIdentifier", "contractIdentifiers", "seasonIdentifier",
            "setIdentifier", "shellIdentifier", "shellSetIdentifier", "variationIdentifier",
            "decoratorIdentifier", "groupIdentifier", "chickenIdentifier", "hatIdentifier",

            "deviceBucket", "deviceLanguage"
        };

        var sensitive = Svc.Redactor.SensitiveFieldNames.ToHashSet(StringComparer.Ordinal);
        var unaccounted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(AuthenticatedMessage).Assembly.GetTypes()
                     .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t))) {
            var descriptor = (MessageDescriptor)type
                .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            foreach (var field in descriptor.Fields.InDeclarationOrder()) {
                if (field.FieldType != FieldType.String) continue;
                if (!piiShaped.IsMatch(field.JsonName)) continue;
                if (sensitive.Contains(field.JsonName) || safe.Contains(field.JsonName)) continue;
                unaccounted.Add($"{descriptor.Name}.{field.JsonName}");
            }
        }

        Assert.Empty(unaccounted);
    }

    [GeneratedRegex(
        "email|secret|password|token|account|device|transaction|receipt|advertising|push|signature|alias|identifier|username|userid|servicesid",
        RegexOptions.IgnoreCase, "en-US")]
    private static partial Regex PiiShapedRegex();
}
