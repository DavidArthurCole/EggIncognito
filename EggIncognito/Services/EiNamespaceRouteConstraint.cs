using System.Text.RegularExpressions;

namespace EggIncognito.Services;

public sealed partial class EiNamespaceRouteConstraint : IRouteConstraint {
    [GeneratedRegex("^ei(_[a-z0-9]+)?$")]
    private static partial Regex Pattern();

    public bool Match(HttpContext? httpContext, IRouter? route, string routeKey, RouteValueDictionary values, RouteDirection routeDirection) =>
        values.TryGetValue(routeKey, out object? value) && value is string ns && Pattern().IsMatch(ns);
}
