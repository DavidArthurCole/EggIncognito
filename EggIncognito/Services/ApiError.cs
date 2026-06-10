// The single error envelope every endpoint returns. `Resolution` is the "possible fix" hint; no error
// ships without one. This is API-envelope JSON via System.Text.Json, not a proto payload, so the
// JsonParser/JsonFormatter.Default rule does not apply here.

namespace EggIncognito.Services;

public sealed record ApiError(string Error, string? Resolution, int Status, object? Details = null);
