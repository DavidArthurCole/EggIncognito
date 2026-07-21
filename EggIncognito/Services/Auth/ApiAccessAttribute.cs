namespace EggIncognito.Services.Auth;

public enum ApiAccessLevel { Public, Authenticated, Contributor, Admin }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ApiAccessAttribute(ApiAccessLevel level) : Attribute
{
    public ApiAccessLevel Level => level;
}
