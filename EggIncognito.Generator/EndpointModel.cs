using System.Collections.Generic;
using System.Linq;

namespace EggIncognito.Generator;

public sealed class EndpointModel
{
    public EndpointModel(string path, string requestType, string responseType, string? rawResponse = null)
    {
        Path = path;
        RequestType = requestType;
        ResponseType = responseType;
        RawResponse = rawResponse;
    }

    public string Path { get; }
    public string RequestType { get; }
    public string ResponseType { get; }
    /// <summary>When set, the endpoint returns this literal string instead of encoded protobuf.</summary>
    public string? RawResponse { get; }
}

public static class EndpointParser
{
    public static List<EndpointModel> Parse(string yaml)
    {
        var results = new List<EndpointModel>();
        string? path = null, requestType = null, responseType = null, rawResponse = null;

        foreach (var rawLine in yaml.Split('\n'))
        {
            var trimmed = rawLine.TrimStart().TrimEnd();

            if (trimmed.StartsWith("- path:"))
            {
                if (path != null)
                    results.Add(Emit(path, requestType, responseType, rawResponse));
                path = trimmed.Substring("- path:".Length).Trim().TrimEnd('/');
                requestType = responseType = rawResponse = null;
            }
            else if (trimmed.StartsWith("requestType:"))
                requestType = trimmed.Substring("requestType:".Length).Trim();
            else if (trimmed.StartsWith("responseType:"))
                responseType = trimmed.Substring("responseType:".Length).Trim();
            else if (trimmed.StartsWith("rawResponse:"))
                rawResponse = trimmed.Substring("rawResponse:".Length).Trim().Trim('"');
        }

        if (path != null)
            results.Add(Emit(path, requestType, responseType, rawResponse));

        return results;
    }

    private static EndpointModel Emit(string path, string? req, string? res, string? raw) =>
        new EndpointModel(
            path,
            req ?? "AuthenticatedMessage",
            res ?? "AuthenticatedMessage",
            raw);

    public static string ToClassName(string path) =>
        string.Concat(
            path.Split(new[] { '/', '_' }, System.StringSplitOptions.RemoveEmptyEntries)
                .Select(s => char.ToUpper(s[0]) + s.Substring(1))
        ) + "Controller";
}
