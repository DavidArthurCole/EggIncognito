using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace EggIncognito.RelayAgent;

public static class Program
{
    public static void Main(string[] args)
    {
        var secret = Environment.GetEnvironmentVariable("RELAY_AGENT_SECRET") ?? "";
        var listen = Environment.GetEnvironmentVariable("RELAY_AGENT_LISTEN") ?? "http://[fd00:8::1]:7779";
        var iface = Environment.GetEnvironmentVariable("RELAY_IFACE") ?? "eth0";
        var prefix = Environment.GetEnvironmentVariable("RELAY_PREFIX") ?? "2a01:4f8:c012:e15b::/64";

        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(listen);
        var app = builder.Build();

        bool Authed(HttpRequest r)
        {
            var h = r.Headers.Authorization.ToString();
            const string p = "Bearer ";
            if (!h.StartsWith(p)) return false;
            var a = Encoding.UTF8.GetBytes(h[p.Length..]);
            var b = Encoding.UTF8.GetBytes(secret);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }

        app.MapGet("/health", () => Results.Ok(new { ok = true }));
        app.MapPost("/provision", async (HttpRequest req) =>
        {
            if (!Authed(req)) return Results.Unauthorized();
            var tail = new StringBuilder();
            foreach (var c in RelayCommands.Provision(prefix, iface))
            {
                var psi = new ProcessStartInfo(c.File) { RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var a in c.Args) psi.ArgumentList.Add(a);
                using var proc = Process.Start(psi)!;
                tail.Append(await proc.StandardError.ReadToEndAsync());
                await proc.WaitForExitAsync();
            }
            return Results.Ok(new { ok = true, tail = tail.ToString() });
        });
        app.Run();
    }
}
