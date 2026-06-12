using System.Reflection;
using System.Text.RegularExpressions;
using Google.Protobuf;
using Google.Protobuf.Reflection;
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

    [Fact]
    public void Redact_ValueWithEscapedQuote_IsConsumedWhole()
    {
        // The value contains \" - the naive [^"]+ pattern stops at the escape and leaks the tail.
        var json = "{ \"deviceName\": \"say \\\" hideout\" }";
        var red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("say", red);
        Assert.DoesNotContain("hideout", red);
        Assert.Contains("redacted-", red);
    }

    [Fact]
    public void Redact_ValueEndingInEscapedBackslash_DoesNotOverrun()
    {
        var json = "{ \"deviceId\": \"trail\\\\\", \"keepMe\": \"visible\" }";
        var red = Svc.Redactor.Redact(json);
        Assert.DoesNotContain("trail", red);
        Assert.Contains("\"keepMe\": \"visible\"", red);
    }

    // The allowlist is hand-maintained, so sweep every Ei.* string field whose name looks PII-shaped
    // and demand it is either in SensitiveFields or explicitly justified as safe below. A new proto
    // field that smells like PII then fails this test until someone makes the call.
    [Fact]
    public void SensitiveFields_CoverEveryPiiLookingProtoStringField()
    {
        var piiShaped = new Regex(
            "email|secret|password|token|account|device|transaction|receipt|advertising|push|signature|alias|identifier|username|userid|servicesid",
            RegexOptions.IgnoreCase);

        // PII-shaped names that are safe to keep in public endpoint JSON, and why.
        var safe = new HashSet<string>(StringComparer.Ordinal)
        {
            // EID-form ids: scrubbed to the EID placeholder by the extractor, not the redactor
            "userId", "eiUserId", "coopUserId", "requestingUserId", "destUserId",
            "toEiUserId", "eiUserIdToKeep", "pastUserIds", "playerIdentifier",
            // public, world-readable game identifiers (contracts, seasons, cosmetics, products)
            "identifier", "contractIdentifier", "contractIdentifiers", "seasonIdentifier",
            "setIdentifier", "shellIdentifier", "shellSetIdentifier", "variationIdentifier",
            "decoratorIdentifier", "groupIdentifier", "chickenIdentifier", "hatIdentifier",
            // coarse hardware class, not a unique identifier
            "deviceBucket",
        };

        var sensitive = Svc.Redactor.SensitiveFieldNames.ToHashSet(StringComparer.Ordinal);
        var unaccounted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(Ei.AuthenticatedMessage).Assembly.GetTypes()
            .Where(t => t.Namespace == "Ei" && !t.IsAbstract && typeof(IMessage).IsAssignableFrom(t)))
        {
            var descriptor = (MessageDescriptor)type
                .GetProperty("Descriptor", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            foreach (var field in descriptor.Fields.InDeclarationOrder())
            {
                if (field.FieldType != FieldType.String) continue;
                if (!piiShaped.IsMatch(field.JsonName)) continue;
                if (sensitive.Contains(field.JsonName) || safe.Contains(field.JsonName)) continue;
                unaccounted.Add($"{descriptor.Name}.{field.JsonName}");
            }
        }

        Assert.Empty(unaccounted);
    }
}
