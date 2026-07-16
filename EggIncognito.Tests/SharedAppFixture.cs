using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

public sealed class SharedAppFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.UseSetting("NoBrowser", "true");
}

[CollectionDefinition(Name)]
public sealed class SharedAppCollection : ICollectionFixture<SharedAppFactory>
{
    public const string Name = "shared-app";
}
