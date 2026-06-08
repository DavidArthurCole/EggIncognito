// EggIncognito.Core/Services/TransportPipeline.cs
//
// The outgoing-request transform pipeline for the Egg, Inc. API, plus the inverse
// (response decode). Owns the AuthenticatedMessage hash so the salt secret stays
// server-side and the browser never reimplements it.
//
// This is the SINGLE home of the hash / wrap logic. The `seed` CLI subcommand and the Inspector
// both call Build(); there is no duplicate copy anymore. Signing parity is locked by the test
// Build_WrappedWithSalt_CodeMatchesSeederAlgorithm.

using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Services;

/// <summary>A single visible step in the request-build or response-decode pipeline.
/// Role lets the UI style/label deterministically instead of parsing the description:
/// "payload" = the raw proto bytes, "envelope" = the AuthenticatedMessage wrapper,
/// "encoding" = a transport encoding step (base64 / form). Skipped marks a no-op stage.</summary>
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
    /// <summary>Whether a signing salt is available (EGG_INC_API_SALT). When false,
    /// AuthenticatedMessage stages are built unsigned and flagged.</summary>
    bool CanSign { get; }

    /// <summary>Build an outgoing request from already-encoded inner proto bytes.</summary>
    BuildResult Build(byte[] innerProtoBytes, bool wrap);

    /// <summary>Decode a base64 response body as the given response message type.</summary>
    DecodeResult Decode(string responseBase64, MessageParser? responseParser);
}

public sealed class TransportPipeline : ITransportPipeline
{
    // Stage roles (see TransportStage.Role).
    private const string RolePayload = "payload";
    private const string RoleEnvelope = "envelope";
    private const string RoleEncoding = "encoding";

    private readonly string? _salt;

    public TransportPipeline(IConfiguration config)
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT") ?? config["EGG_INC_API_SALT"]) { }

    // For non-DI callers (the Seeder CLI): take the salt straight from the EGG_INC_API_SALT env var,
    // so the signing logic has a single home here instead of a duplicated copy in the Seeder.
    public TransportPipeline()
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT")) { }

    private TransportPipeline(string? salt) => _salt = salt;

    public bool CanSign => !string.IsNullOrEmpty(_salt);

    public BuildResult Build(byte[] innerProtoBytes, bool wrap)
    {
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
            if (CanSign)
            {
                wrapped = WrapInAuthMessage(innerProtoBytes, _salt!);
            }
            else
            {
                // Build the wrapper with an empty code so the shape is still visible.
                wrapped = new Ei.AuthenticatedMessage
                {
                    Message = ByteString.CopyFrom(innerProtoBytes),
                }.ToByteArray();
                note = "UNSIGNED - EGG_INC_API_SALT not set; real-API sends requiring auth will fail. " + envelopeNote;
            }
            stages.Add(Stage("authenticated-message",
                "Wrapped in AuthenticatedMessage { message, code = SHA256 hash }",
                wrapped, note, role: RoleEnvelope));
            postBytes = wrapped;
        }
        else
        {
            // Not skipped in the empty sense: the request IS the proto bytes, posted as-is.
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

        // The real auxbrain API wraps responses in an AuthenticatedMessage (optionally
        // compressed). The EggIncognito mock returns the raw response message directly.
        // Try the wrapped path first; if the inner payload doesn't parse as the response
        // type, fall back to parsing the response bytes directly.
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
        try
        {
            var outer = Ei.AuthenticatedMessage.Parser.ParseFrom(respBytes);
            var messageBytes = outer.Message.ToByteArray();
            if (messageBytes.Length == 0) return null;

            var inner = outer.Compressed ? EndpointExtractor.Decompress(messageBytes) : messageBytes;
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
        catch
        {
            return null;
        }
    }

    private static TransportStage Stage(string name, string desc, byte[] bytes, string? note = null, string? role = null) =>
        new(name, desc, bytes.Length, Convert.ToHexString(bytes).ToLowerInvariant(),
            Convert.ToBase64String(bytes), note, role);

    // --- the canonical AuthenticatedMessage signing logic (single source of truth) ---

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
        mutated[magic % (uint)mutated.Length] = 0x1b;

        var combined = new byte[mutated.Length + salt.Length];
        mutated.CopyTo(combined, 0);
        salt.CopyTo(combined, mutated.Length);
        return Convert.ToHexString(SHA256.HashData(combined)).ToLowerInvariant();
    }
}
