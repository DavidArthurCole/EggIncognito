using System.Runtime.CompilerServices;
using System.Text.Json;

namespace EggIncognito.Services.Backfill;

public interface IGitHubClient
{
    IAsyncEnumerable<GitHubClient.Commit> CommitsAsync(string repo, CancellationToken ct = default);
    Task<string?> FileAtAsync(string repo, string sha, string[] paths, CancellationToken ct = default);
}


public sealed class GitHubClient(IHttpClientFactory httpFactory, IConfiguration config) : IGitHubClient
{
    private HttpClient Client()
    {
        var c = httpFactory.CreateClient("github");
        c.DefaultRequestHeaders.UserAgent.ParseAdd("EggIncognito-backfill");
        var token = config["GITHUB_TOKEN"];
        if (!string.IsNullOrWhiteSpace(token))
            c.DefaultRequestHeaders.Authorization = new("Bearer", token);
        return c;
    }

    public sealed record Commit(string Sha, string Message, DateTimeOffset Date);

    public async IAsyncEnumerable<Commit> CommitsAsync(
        string repo, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var c = Client();
        for (var page = 1; ; page++)
        {
            var res = await c.GetAsync($"https://api.github.com/repos/{repo}/commits?per_page=100&page={page}", ct);
            if (!res.IsSuccessStatusCode) yield break;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var arr = doc.RootElement;
            if (arr.GetArrayLength() == 0) yield break;
            foreach (var e in arr.EnumerateArray())
            {
                var sha = e.GetProperty("sha").GetString()!;
                var commit = e.GetProperty("commit");
                var msg = commit.GetProperty("message").GetString() ?? "";
                var date = commit.GetProperty("committer").GetProperty("date").GetDateTimeOffset();
                yield return new Commit(sha, msg.Split('\n')[0], date);
            }
        }
    }

   
    public async Task<string?> FileAtAsync(string repo, string sha, string[] paths, CancellationToken ct = default)
    {
        var c = Client();
        foreach (var path in paths)
        {
            var res = await c.GetAsync($"https://raw.githubusercontent.com/{repo}/{sha}/{path}", ct);
            if (res.IsSuccessStatusCode) return await res.Content.ReadAsStringAsync(ct);
        }
        return null;
    }
}
