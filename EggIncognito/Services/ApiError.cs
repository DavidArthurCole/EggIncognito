// The single error envelope every endpoint returns. `Resolution` is the "possible
// fix" hint - the app's rule is that no error ships without one. This is API-envelope
// JSON (System.Text.Json), NOT a proto payload, so the JsonParser/JsonFormatter.Default
// rule does not apply here.

namespace EggIncognito.Services;

public sealed record ApiError(string Error, string? Resolution, int Status, object? Details = null);
