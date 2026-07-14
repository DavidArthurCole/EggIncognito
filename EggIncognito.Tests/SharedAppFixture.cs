using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace EggIncognito.Tests;

// One WebApplicationFactory<Program> shared across every integration class whose only host tweak was
// NoBrowser=true. Booting the Blazor host is the dominant cost in the suite (~0.4s/boot); collapsing
// ~20 identical boots into one shaves the integration slice from seconds to near-instant. Classes that
// need distinct config (AppMode=Hosted, auth creds, rate-limit tuning) keep their own IClassFixture.
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
