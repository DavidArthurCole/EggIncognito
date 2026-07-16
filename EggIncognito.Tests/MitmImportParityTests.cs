using System.Text;
using System.Text.Json;
using EggIncognito.Services;
using Google.Protobuf;

namespace EggIncognito.Tests;

public class MitmImportParityTests
{
    private const string Url = "https://www.auxbrain.com/ei/get_periodicals";
    private const string Slug = "ei/get_periodicals";

    private const string Yaml = """
routes:
  - path: ei/get_periodicals
    request: GetPeriodicalsRequest
    response: PeriodicalsResponse

needs_capture:
  request_unknown:
""";

    private static string MakeRepo() => TestRepoFixture.MakeRepo(Yaml, "ei-mitm");

    private static byte[] WrappedResponse()
    {
        var inner = new Ei.PeriodicalsResponse();
        var outer = new Ei.AuthenticatedMessage { Message = inner.ToByteString(), Compressed = false };
        return outer.ToByteArray();
    }

    private static string EndpointPath(string root) =>
        Path.Combine(root, "Endpoints", "default", Slug + ".json");

    [Fact]
    public void RunFromMitm_MatchesHarPath_ByteForByte()
    {
        var respBytes = WrappedResponse();
        var respB64 = Convert.ToBase64String(respBytes);

       
        var harRoot = MakeRepo();
        var harFile = Path.Combine(harRoot, "session.har");
        File.WriteAllText(harFile, BuildHar(Url, respB64), new UTF8Encoding(false));
        var har = EndpointExtractor.ForRepo(harRoot, null, "EI0000000000000000", false);
        har.RunFromHar(harFile);
        har.Save();

       
        var mitmRoot = MakeRepo();
        var mitmFile = Path.Combine(mitmRoot, "session.mitm");
        File.WriteAllBytes(mitmFile, BuildMitm(Url, "data=" + Uri.EscapeDataString(respB64), respBytes));
        var mitm = EndpointExtractor.ForRepo(mitmRoot, null, "EI0000000000000000", false);
        mitm.RunFromMitm(mitmFile);
        mitm.Save();

        Assert.True(File.Exists(EndpointPath(mitmRoot)));
        Assert.Equal(File.ReadAllText(EndpointPath(harRoot)), File.ReadAllText(EndpointPath(mitmRoot)));
        Assert.Equal(1, mitm.Counts.Wrote);
        Assert.Equal(har.Counts.Wrote, mitm.Counts.Wrote);
    }

    private static string BuildHar(string url, string responseBodyB64)
    {
        var har = new
        {
            log = new
            {
                version = "1.2",
                entries = new[]
                {
                    new
                    {
                        request = new
                        {
                            method = "POST",
                            url,
                            postData = new { mimeType = "application/x-www-form-urlencoded", @params = Array.Empty<object>() },
                        },
                        response = new { status = 200, content = new { text = responseBodyB64 } },
                    },
                },
            },
        };
        return JsonSerializer.Serialize(har);
    }

   
    private static byte[] BuildMitm(string url, string requestBody, byte[] responseContent)
    {
        var uri = new Uri(url);
        var req = TnetDict(
            ("method", TnetStr("POST")),
            ("scheme", TnetStr(uri.Scheme)),
            ("host", TnetStr(uri.Host)),
            ("port", TnetInt(uri.Port)),
            ("path", TnetStr(uri.AbsolutePath)),
            ("content", TnetBytes(Encoding.UTF8.GetBytes(requestBody))));
        var res = TnetDict(
            ("status_code", TnetInt(200)),
            ("content", TnetBytes(responseContent)));
        return TnetDict(("type", TnetStr("http")), ("request", req), ("response", res));
    }

   

    private static byte[] TnetStr(string s) => TnetBytes(Encoding.UTF8.GetBytes(s));

    private static byte[] TnetBytes(byte[] payload)
    {
        var prefix = Encoding.ASCII.GetBytes($"{payload.Length}:");
        var buf = new byte[prefix.Length + payload.Length + 1];
        prefix.CopyTo(buf, 0);
        payload.CopyTo(buf, prefix.Length);
        buf[^1] = (byte)',';
        return buf;
    }

    private static byte[] TnetInt(long n)
    {
        var s = n.ToString();
        return Encoding.ASCII.GetBytes($"{s.Length}:{s}#");
    }

    private static byte[] TnetDict(params (string key, byte[] value)[] pairs)
    {
        using var payload = new MemoryStream();
        foreach (var (key, value) in pairs)
        {
            var k = TnetStr(key);
            payload.Write(k, 0, k.Length);
            payload.Write(value, 0, value.Length);
        }
        var inner = payload.ToArray();
        var prefix = Encoding.ASCII.GetBytes($"{inner.Length}:");
        var buf = new byte[prefix.Length + inner.Length + 1];
        prefix.CopyTo(buf, 0);
        inner.CopyTo(buf, prefix.Length);
        buf[^1] = (byte)'}';
        return buf;
    }
}
