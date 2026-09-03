using System.Text;
using Ei;
using Google.Protobuf;
using Microsoft.Extensions.Configuration;

namespace EggIncognito.Core.Services;

public sealed record TransportStage(
    string Name,
    string Description,
    int ByteLength,
    string? Hex,
    string? Base64,
    string? Note = null,
    string? Role = null,
    bool Skipped = false);

public sealed record BuildResult(
    IReadOnlyList<TransportStage> Stages,
    string FinalBase64,
    string FinalFormBody);

public sealed record DecodeResult(
    IReadOnlyList<TransportStage> Stages,
    string? Json,
    string? Error,
    bool WrappedMismatch = false);

public interface ITransportPipeline {
    bool CanSign { get; }

    BuildResult Build(byte[] innerProtoBytes, bool wrap);

    BuildResult Build(byte[] innerProtoBytes, bool wrap, string? salt);

    DecodeResult Decode(string responseBase64, MessageParser? responseParser, bool? responseWrapped = null);
}

public sealed class TransportPipeline : ITransportPipeline {
    private const string RolePayload = "payload";
    private const string RoleEnvelope = "envelope";
    private const string RoleEncoding = "encoding";

    private readonly string? _salt;
    private readonly IEnumFailover? _enumFailover;

    public TransportPipeline(IConfiguration config, IEnumFailover enumFailover)
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT") ?? config["EGG_INC_API_SALT"], enumFailover) {
    }

    public TransportPipeline(IConfiguration config)
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT") ?? config["EGG_INC_API_SALT"], null) {
    }

    public TransportPipeline()
        : this(Environment.GetEnvironmentVariable("EGG_INC_API_SALT"), null) {
    }

    private TransportPipeline(string? salt, IEnumFailover? enumFailover) {
        _salt = salt;
        _enumFailover = enumFailover;
    }

    public bool CanSign => !string.IsNullOrEmpty(_salt);

    public BuildResult Build(byte[] innerProtoBytes, bool wrap) => Build(innerProtoBytes, wrap, _salt);

    public BuildResult Build(byte[] innerProtoBytes, bool wrap, string? salt) {
        bool canSign = !string.IsNullOrEmpty(salt);
        var stages = new List<TransportStage> {
            Stage("proto-encode", "Request message serialized to protobuf wire bytes",
                innerProtoBytes, role: RolePayload)
        };

        const string envelopeNote =
            "AuthenticatedMessage fields: message = the inner proto bytes (the payload above); " +
            "code = SHA256(message + salt) signature; compressed/originalSize used when the payload is gzipped.";

        byte[] postBytes;
        if (wrap) {
            string? note = envelopeNote;
            byte[] wrapped;
            if (canSign) {
                wrapped = WrapInAuthMessage(innerProtoBytes, salt!);
            } else {
                wrapped = new AuthenticatedMessage {
                    Message = ByteString.CopyFrom(innerProtoBytes)
                }.ToByteArray();
                note = "UNSIGNED - no signing salt provided; real-API sends requiring auth will fail. " + envelopeNote;
            }

            stages.Add(Stage("authenticated-message",
                "Wrapped in AuthenticatedMessage { message, code = SHA256 hash }",
                wrapped, note, RoleEnvelope));
            postBytes = wrapped;
        } else {
            stages.Add(Stage("passthrough",
                "Posted as-is - this endpoint does not wrap the request in an AuthenticatedMessage",
                innerProtoBytes, role: RolePayload));
            postBytes = innerProtoBytes;
        }

        string b64 = Convert.ToBase64String(postBytes);
        stages.Add(new TransportStage("base64",
            "Base64-encode the POST bytes",
            postBytes.Length, null, b64, Role: RoleEncoding));

        string formBody = "data=" + Uri.EscapeDataString(b64);
        stages.Add(new TransportStage("form-urlencode",
            "application/x-www-form-urlencoded body: data=<base64>",
            postBytes.Length, null, null, formBody, RoleEncoding));

        return new BuildResult(stages, b64, formBody);
    }

    public DecodeResult Decode(string responseBase64, MessageParser? responseParser, bool? responseWrapped = null) {
        var stages = new List<TransportStage>();
        byte[] respBytes;
        try {
            respBytes = ProtoFraming.FromBase64Loose(responseBase64);
        } catch (Exception ex) {
            return new DecodeResult(stages, null, $"not valid base64: {ex.Message}");
        }

        stages.Add(Stage("base64-decode", "Decode base64 response body", respBytes, role: RoleEncoding));

        if (responseParser is null)
            return new DecodeResult(stages, null, "no parser for this endpoint's response type");

        if (responseWrapped != false) {
            var wrapped = TryDecodeWrapped(respBytes, responseParser, responseWrapped == true);
            if (wrapped is not null) {
                stages.AddRange(wrapped.Value.Stages);
                return new DecodeResult(stages, wrapped.Value.Json, null);
            }
        }

        DecodeResult? WrappedFallback() {
            if (responseWrapped != false) return null;
            string note = "Response is AuthenticatedMessage-wrapped; this route declares responseWrapped: false. Update the route flag.";
            var fallback = TryDecodeWrapped(respBytes, responseParser, envelopeNote: note);
            if (fallback is null) return null;
            stages.AddRange(fallback.Value.Stages);
            return new DecodeResult(stages, fallback.Value.Json, null, WrappedMismatch: true);
        }

        try {
            var msg = responseParser.ParseFrom(respBytes);
            if (respBytes.Length > 0 && !HasAnyKnownField(msg) && WrappedFallback() is { } rescuedEmpty)
                return rescuedEmpty;

            string? json = FormatJson(msg);
            stages.Add(new TransportStage("proto-decode",
                "Parsed bytes directly as the endpoint's response message (unwrapped - e.g. EggIncognito mock)",
                respBytes.Length, null, null, "see JSON below", RolePayload));
            return new DecodeResult(stages, json, null);
        } catch (Exception ex) {
            return WrappedFallback() ?? new DecodeResult(stages, null, $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    private static bool HasAnyKnownField(IMessage msg) {
        foreach (var f in msg.Descriptor.Fields.InFieldNumberOrder()) {
            if (f.IsRepeated) {
                if (f.Accessor.GetValue(msg) is System.Collections.ICollection { Count: > 0 }) return true;
            } else if (f.Accessor.HasValue(msg)) {
                return true;
            }
        }

        return false;
    }

    private string FormatJson(IMessage msg) {
        string json = JsonFormatter.Default.Format(msg);
        return _enumFailover is null ? json : _enumFailover.Apply(msg, json);
    }

    private (IReadOnlyList<TransportStage> Stages, string Json)? TryDecodeWrapped(
        byte[] respBytes, MessageParser responseParser, bool force = false, string? envelopeNote = null) {
        if (!force && !LooksLikeAuthEnvelope(respBytes)) return null;
        try {
            var outer = AuthenticatedMessage.Parser.ParseFrom(respBytes);
            byte[]? messageBytes = outer.Message.ToByteArray();
            if (messageBytes.Length == 0) return null;

            byte[] inner = outer.Compressed ? ProtoFraming.Decompress(messageBytes) : messageBytes;
            var msg = responseParser.ParseFrom(inner);
            string? json = FormatJson(msg);

            var stages = new List<TransportStage> {
                Stage("authenticated-message",
                    $"Parsed AuthenticatedMessage (compressed = {outer.Compressed})", messageBytes,
                    envelopeNote, RoleEnvelope)
            };
            if (outer.Compressed)
                stages.Add(Stage("inflate", "Decompressed inner payload (gzip/zlib)", inner, role: RoleEncoding));
            stages.Add(new TransportStage("proto-decode",
                "Parsed inner payload as the endpoint's response message",
                inner.Length, null, null, "see JSON below", RolePayload));
            return (stages, json);
        } catch (Exception ex) when (ex is InvalidProtocolBufferException or InvalidDataException) {
            return null;
        }
    }

    private static bool LooksLikeAuthEnvelope(byte[] bytes) {
        try {
            using var input = new CodedInputStream(bytes);
            uint tag;
            while ((tag = input.ReadTag()) != 0) {
                int field = WireFormat.GetTagFieldNumber(tag);
                var wire = WireFormat.GetTagWireType(tag);
                bool known = (field, wire) switch {
                    (1 or 2 or 6, WireFormat.WireType.LengthDelimited) => true,
                    (3 or 4 or 5, WireFormat.WireType.Varint) => true,
                    _ => false
                };
                if (!known) return false;
                input.SkipLastField();
            }

            return true;
        } catch (InvalidProtocolBufferException) {
            return false;
        }
    }

    private static TransportStage Stage(string name, string desc, byte[] bytes, string? note = null,
        string? role = null) =>
        new(name, desc, bytes.Length, Convert.ToHexString(bytes).ToLowerInvariant(),
            Convert.ToBase64String(bytes), note, role);

    private static byte[] WrapInAuthMessage(byte[] innerBytes, string salt) {
        var msg = new AuthenticatedMessage {
            Message = ByteString.CopyFrom(innerBytes),
            Code = ComputeCode(innerBytes, salt)
        };
        return msg.ToByteArray();
    }

    private static string ComputeCode(byte[] messageBytes, string phrase) {
        byte[] salt = Encoding.ASCII.GetBytes(Hashes.Sha256Hex(phrase));

        const uint magic = 0x3b9af419;
        byte[] mutated = (byte[])messageBytes.Clone();

        if (mutated.Length > 0)
            mutated[magic % (uint)mutated.Length] = 0x1b;

        byte[] combined = new byte[mutated.Length + salt.Length];
        mutated.CopyTo(combined, 0);
        salt.CopyTo(combined, mutated.Length);
        return Hashes.Sha256Hex(combined);
    }
}
