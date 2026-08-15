using EggIncognito.Services.Theme;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace EggIncognito.Tests;

public static class ThemeTestSupport {
    public static ThemeCssSerializer Serializer(string environment = "Production") =>
        new(new FakeWebHostEnvironment(environment), NullLogger<ThemeCssSerializer>.Instance);

    public static ThemeModel WithCss(this ThemeModel model, string css) => model with { Css = css };

    private sealed class FakeWebHostEnvironment(string environmentName) : IWebHostEnvironment {
        public string ApplicationName { get; set; } = "EggIncognito.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = environmentName;
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
