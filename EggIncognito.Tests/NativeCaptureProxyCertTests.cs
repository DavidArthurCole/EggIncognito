using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using EggIncognito.Capture;

namespace EggIncognito.Tests;

public class NativeCaptureProxyCertTests {
    [Fact]
    public void MintedLeaf_HasPrivateKeyAndSan() {
        using var leaf = NativeCaptureProxy.MintLeafForTest("www.auxbrain.com", out var root);
        using (root) {
            Assert.True(leaf.HasPrivateKey);
            Assert.Equal("CN=www.auxbrain.com", leaf.Subject);
            var san = leaf.Extensions.OfType<X509Extension>().FirstOrDefault(e => e.Oid?.Value == "2.5.29.17");
            Assert.NotNull(san);

            Assert.True(leaf.NotAfter <= root.NotAfter);
        }
    }

    [Fact]
    public async Task MintedLeaf_CompletesSslStreamServerHandshake() {
        using var leaf = NativeCaptureProxy.MintLeafForTest("www.auxbrain.com", out var root);
        using var _ = root;

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () => {
            using var s = await listener.AcceptTcpClientAsync();
            await using var tls = new SslStream(s.GetStream(), false);
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions {
                ServerCertificate = leaf,
                ApplicationProtocols = [SslApplicationProtocol.Http11]
            });

            byte[] buf = new byte[1];
            await tls.ReadExactlyAsync(buf);
            await tls.WriteAsync(buf);
        });

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port);
        await using var ctls = new SslStream(client.GetStream(), false, (_, cert, chain, errors) => {
            using var c2 = new X509Chain();
            c2.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            c2.ChainPolicy.CustomTrustStore.Add(root);
            c2.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            return c2.Build(new X509Certificate2(cert!));
        });

        await ctls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions {
            TargetHost = "www.auxbrain.com",
            ApplicationProtocols = [SslApplicationProtocol.Http11]
        });

        Assert.Equal(SslApplicationProtocol.Http11, ctls.NegotiatedApplicationProtocol);
        await ctls.WriteAsync("*"u8.ToArray());
        byte[] back = new byte[1];
        await ctls.ReadExactlyAsync(back);
        Assert.Equal(42, back[0]);

        await serverTask;
        listener.Stop();
    }
}
