using System.Text;
using Google.Protobuf;
using Microsoft.AspNetCore.Mvc;
using EggIncognito.Services;

namespace EggIncognito.Controllers;

[ApiController]
public abstract class MockApiControllerBase(IEndpointStore endpoints, IBehaviorService behaviors) : ControllerBase
{
    private readonly IEndpointStore _endpoints = endpoints;
    private readonly IBehaviorService _behaviors = behaviors;

    protected Task<IActionResult> HandleAsync<TRes>(string path, string? data, string? sim = null)
        where TRes : IMessage<TRes>, new()
    {
        if (sim is not null)
        {
            var behavior = _behaviors.Get(sim);
            if (behavior is null)
            {
                var valid = _behaviors.All().Select(b => b.Name).ToArray();
                throw new ApiException(
                    $"unknown sim '{sim}'",
                    "Use one of the valid sim names listed in details, or omit ?sim to get the endpoint response.",
                    StatusCodes.Status400BadRequest,
                    new { valid });
            }
            foreach (var kvp in behavior.ExtraHeaders ?? new Dictionary<string, string>())
                Response.Headers[kvp.Key] = kvp.Value;
            var bodyStr = behavior.Body is not null
                ? Encoding.UTF8.GetString(behavior.Body())
                : string.Empty;
            var contentType = behavior.HttpStatus is >= 200 and < 300 ? "text/html" : "text/plain";
            return Task.FromResult<IActionResult>(new ContentResult
            {
                StatusCode = behavior.HttpStatus,
                Content = bodyStr,
                ContentType = contentType,
            });
        }
        string? eid = ExtractEid(data);
        var response = _endpoints.Get<TRes>(path, eid);
        var encoded = Convert.ToBase64String(response.ToByteArray());
        return Task.FromResult<IActionResult>(Content(encoded, "text/html"));
    }

    protected Task<IActionResult> HandleRawAsync(string body, string? sim = null)
    {
        if (sim is not null)
        {
            var behavior = _behaviors.Get(sim);
            if (behavior is null)
            {
                var valid = _behaviors.All().Select(b => b.Name).ToArray();
                throw new ApiException(
                    $"unknown sim '{sim}'",
                    "Use one of the valid sim names listed in details, or omit ?sim to get the endpoint response.",
                    StatusCodes.Status400BadRequest,
                    new { valid });
            }
            foreach (var kvp in behavior.ExtraHeaders ?? new Dictionary<string, string>())
                Response.Headers[kvp.Key] = kvp.Value;
            var bodyStr = behavior.Body is not null
                ? Encoding.UTF8.GetString(behavior.Body())
                : string.Empty;
            var contentType = behavior.HttpStatus is >= 200 and < 300 ? "text/html" : "text/plain";
            return Task.FromResult<IActionResult>(new ContentResult
            {
                StatusCode = behavior.HttpStatus,
                Content = bodyStr,
                ContentType = contentType,
            });
        }
        return Task.FromResult<IActionResult>(Content(body, "text/plain"));
    }

    private static string? ExtractEid(string? data)
    {
        if (data is null) return null;
        try
        {
            var bytes = Convert.FromBase64String(data);
            var msg = Ei.AuthenticatedMessage.Parser.ParseFrom(bytes);
            return string.IsNullOrEmpty(msg.UserId) ? null : msg.UserId;
        }
        catch
        {
            return null;
        }
    }
}
