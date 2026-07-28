using System.Reflection;
using EggIncognito.Services.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;

namespace EggIncognito.Tests;

public class ApiAccessGuardTests {
    [Fact]
    public void EveryController_DeclaresApiAccessPolicy() {
        var asm = typeof(Program).Assembly;
        var controllers = asm.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

        var missing = new List<string>();
        foreach (var t in controllers) {
            if (t.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0) continue;

            var actions = t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes(typeof(HttpMethodAttribute), true).Length > 0)
                .ToArray();

            if (actions.Length > 0 &&
                actions.All(m => m.GetCustomAttributes(typeof(ApiAccessAttribute), true).Length > 0)) {
                continue;
            }

            missing.Add(t.Name);
        }

        Assert.True(missing.Count == 0, "controllers missing [ApiAccess]: " + string.Join(", ", missing));
    }
}
