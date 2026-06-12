// The outgoing-request transform pipeline for the Egg, Inc. API, plus the inverse response decode.
// Owns the AuthenticatedMessage hash so the salt secret stays server-side and the browser never
// reimplements it. The single home of the hash/wrap logic: the Inspector and any non-DI caller both
// call Build(). Signing parity is locked by Build_WrappedWithSalt_CodeMatchesSeederAlgorithm.

using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

/// <summary>A single visible step in the request-build or response-decode pipeline.
/// Role lets the UI style/label deterministically instead of parsing the description:
/// "payload" = the raw proto bytes, "envelope" = the AuthenticatedMessage wrapper,
/// "encoding" = a transport encoding step. Skipped marks a no-op stage.</summary>
public sealed record TransportStage(
    string Name,
    string Description,
    int ByteLength,
    string? Hex,
    string? Base64,
    string? Note = null,
    string? Role = null,
    bool Skipped = false);

/// <summary>Result of building an outgoing request, with every transform exposed.</summary>
public sealed record BuildResult(
    IReadOnlyList<TransportStage> Stages,
    string FinalBase64,
    string FinalFormBody);

/// <summary>Result of decoding a response, with every transform exposed.</summary>
public sealed record DecodeResult(
    IReadOnlyList<TransportStage> Stages,
    string? Json,
    string? Error);

public interface ITransportPipeline
{
    /// <summary>Whether a signing salt is available. When false, AuthenticatedMessage stages are built
    /// unsigned and flagged.</summary>
    bool CanSign { get; }

    /// <summary>Build an outgoing request from already-encoded inner proto bytes.</summary>
    BuildResult Build(byte[] innerProtoBytes, bool wrap);

    /// <summary>Build an outgoing request, signing any AuthenticatedMessage wrapper with the
    /// caller-supplied salt instead of the instance/env salt. A null or empty salt builds unsigned.</summary>
    BuildResult Build(byte[] innerProtoBytes, bool wrap, string? salt);

    /// <summary>Decode a base64 response body as the given response message type.</summary>
    DecodeResult Decode(string responseBase64, MessageParser? responseParser);
}

public sealed class TransportPipeline : ITransportPipeline
{
    // Stage roles.
    private const string RolePayload = "payload";
    private const string RoleEnvelope = "envelope";
    private const string RoleEncoding = "encoding";

    private readonly string? _salt;

    public TransportPipeline(IConfiguration config)
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT") ?? config["EGG_INC_API_SALT"]) { }

    // For non-DI callers: take the salt straight from the EGG_INC_API_SALT env var.
    public TransportPipeline()
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT")) { }

    private TransportPipeline(string? salt) => _salt = salt;

    public bool CanSign => !string.IsNullOrEmpty(_salt);

    public BuildResult Build(byte[] innerProtoBytes, bool wrap) => Build(innerProtoBytes, wrap, _salt);

    public BuildResult Build(byte[] innerProtoBytes, bool wrap, string? salt)
    {
        var canSign = !string.IsNullOrEmpty(salt);
        var stages = new List<TransportStage>
        {
            Stage("proto-encode", "Request message serialized to protobuf wire bytes",
                innerProtoBytes, role: RolePayload),
        };

        const string envelopeNote =
            "AuthenticatedMessage fields: message = the inner proto bytes (the payload above); " +
            "code = SHA256(message + salt) signature; compressed/originalSize used when the payload is gzipped.";

        byte[] postBytes;
        if (wrap)
        {
            string? note = envelopeNote;
            byte[] wrapped;
            if (canSign)
            {
                wrapped = WrapInAuthMessage(innerProtoBytes, salt!);
            }
            else
            {
                // Build the wrapper with an empty code so the shape stays visible.
                wrapped = new Ei.AuthenticatedMessage
                {
                    Message = ByteString.CopyFrom(innerProtoBytes),
                }.ToByteArray();
                note = "UNSIGNED - no signing salt provided; real-API sends requiring auth will fail. " + envelopeNote;
            }
            stages.Add(Stage("authenticated-message",
                "Wrapped in AuthenticatedMessage { message, code = SHA256 hash }",
                wrapped, note, role: RoleEnvelope));
            postBytes = wrapped;
        }
        else
        {
            // The request is the proto bytes, posted as-is.
            stages.Add(Stage("passthrough",
                "Posted as-is - this endpoint does not wrap the request in an AuthenticatedMessage",
                innerProtoBytes, role: RolePayload));
            postBytes = innerProtoBytes;
        }

        var b64 = Convert.ToBase64String(postBytes);
        stages.Add(new TransportStage("base64",
            "Base64-encode the POST bytes",
            postBytes.Length, null, b64, Role: RoleEncoding));

        var formBody = "data=" + Uri.EscapeDataString(b64);
        stages.Add(new TransportStage("form-urlencode",
            "application/x-www-form-urlencoded body: data=<base64>",
            postBytes.Length, null, null, formBody, Role: RoleEncoding));

        return new BuildResult(stages, b64, formBody);
    }

    public DecodeResult Decode(string responseBase64, MessageParser? responseParser)
    {
        var stages = new List<TransportStage>();
        byte[] respBytes;
        try
        {
            respBytes = ProtoFraming.FromBase64Loose(responseBase64);
        }
        catch (Exception ex)
        {
            return new DecodeResult(stages, null, $"not valid base64: {ex.Message}");
        }
        stages.Add(Stage("base64-decode", "Decode base64 response body", respBytes, role: RoleEncoding));

        if (responseParser is null)
            return new DecodeResult(stages, null, "no parser for this endpoint's response type");

        // The real auxbrain API wraps responses in an AuthenticatedMessage, optionally compressed. The
        // EggIncognito mock returns the raw response message directly. Try the wrapped path first (gated
        // on the bytes matching the envelope wire shape, see LooksLikeAuthEnvelope); if the inner payload
        // doesn't parse as the response type, fall back to the response bytes directly.
        var wrapped = TryDecodeWrapped(respBytes, responseParser);
        if (wrapped is not null)
        {
            stages.AddRange(wrapped.Value.Stages);
            return new DecodeResult(stages, wrapped.Value.Json, null);
        }

        try
        {
            var msg = responseParser.ParseFrom(respBytes);
            var json = JsonFormatter.Default.Format(msg);
            stages.Add(new TransportStage("proto-decode",
                "Parsed bytes directly as the endpoint's response message (unwrapped - e.g. EggIncognito mock)",
                respBytes.Length, null, null, "see JSON below", Role: RolePayload));
            return new DecodeResult(stages, json, null);
        }
        catch (Exception ex)
        {
            return new DecodeResult(stages, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    // Returns null if the bytes are not a usable AuthenticatedMessage-wrapped response.
    private (IReadOnlyList<TransportStage> Stages, string Json)? TryDecodeWrapped(
        byte[] respBytes, MessageParser responseParser)
    {
        // Protobuf parsing is permissive: an unwrapped response whose field 1 is length-delimited can
        // also "parse" as an AuthenticatedMessage, mislabeling a mock response as wrapped and silently
        // dropping its other fields. The envelope schema is frozen, so require every top-level field to
        // match it before committing to the wrapped path.
        if (!LooksLikeAuthEnvelope(respBytes)) return null;
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
            var messageBytes = outer.Message.ToByteArray();
            if (messageBytes.Length == 0) return null;

            var inner = outer.Compressed ? ProtoFraming.Decompress(messageBytes) : messageBytes;
            var msg = responseParser.ParseFrom(inner);
            var json = JsonFormatter.Default.Format(msg);

            var stages = new List<TransportStage>
            {
                Stage("authenticated-message",
                    $"Parsed AuthenticatedMessage (compressed = {outer.Compressed})", messageBytes, role: RoleEnvelope),
            };
            if (outer.Compressed)
                stages.Add(Stage("inflate", "Decompressed inner payload (gzip/zlib)", inner, role: RoleEncoding));
            stages.Add(new TransportStage("proto-decode",
                "Parsed inner payload as the endpoint's response message",
                inner.Length, null, null, "see JSON below", Role: RolePayload));
            return (stages, json);
        }
        catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidDataException)
        {
            // The expected "not actually wrapped" signals: malformed envelope/inner proto or a corrupt
            // compressed payload. The caller falls back to the direct parse. Anything else propagates
            // instead of masquerading as a direct-parse failure.
            return null;
        }
    }

    // True when every top-level field of the bytes matches the AuthenticatedMessage wire shape
    // (message=1/code=2/user_id=6 length-delimited; version=3/compressed=4/original_size=5 varint).
    // Any field outside that frozen envelope schema means the bytes are an unwrapped response, even
    // though a lenient ParseFrom would tolerate it as an unknown field.
    private static bool LooksLikeAuthEnvelope(byte[] bytes)
    {
        try
        {
            using var input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0)
            {
                int field = WireFormat.GetTagFieldNumber(tag);
                var wire = WireFormat.GetTagWireType(tag);
                bool known = (field, wire) switch
                {
                    (1 or 2 or 6, WireFormat.WireType.LengthDelimited) => true,
                    (3 or 4 or 5, WireFormat.WireType.Varint) => true,
                    _ => false,
                };
                if (!known) return false;
                input.SkipLastField();
            }
            return true;
        }
        catch (InvalidProtocolBufferException) { return false; }
    }

    private static TransportStage Stage(string name, string desc, byte[] bytes, string? note = null, string? role = null) =>
        new(name, desc, bytes.Length, Convert.ToHexString(bytes).ToLowerInvariant(),
            Convert.ToBase64String(bytes), note, role);

    // The canonical AuthenticatedMessage signing logic - single source of truth.
    private static byte[] WrapInAuthMessage(byte[] innerBytes, string salt)
    {
        var msg = new Ei.AuthenticatedMessage
        {
            Message = ByteString.CopyFrom(innerBytes),
            Code = ComputeCode(innerBytes, salt),
        };
        return msg.ToByteArray();
    }

    private static string ComputeCode(byte[] messageBytes, string phrase)
    {
        var phraseHash = SHA256.HashData(Encoding.UTF8.GetBytes(phrase));
        var salt = Encoding.ASCII.GetBytes(Convert.ToHexString(phraseHash).ToLowerInvariant());

        const uint magic = 0x3b9af419;
        var mutated = (byte[])messageBytes.Clone();
        // A zero-length message has no byte to mutate, and `magic % 0` divides by zero. An all-default
        // proto serializes to 0 bytes, which the Inspector can send; sign it as-is without the flip.
        if (mutated.Length > 0)
            mutated[magic % (uint)mutated.Length] = 0x1b;

        var combined = new byte[mutated.Length + salt.Length];
        mutated.CopyTo(combined, 0);
        salt.CopyTo(combined, mutated.Length);
        return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
    }
}
