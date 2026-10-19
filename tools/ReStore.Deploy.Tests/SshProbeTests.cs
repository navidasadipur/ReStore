using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Probe = ReStore.SshProbe.SshProbe;

namespace ReStore.Deploy.Tests;

public class SshProbeTests
{
    [Theory]
    [InlineData("", 22)]
    [InlineData("-host", 22)]
    [InlineData("bad host", 22)]
    [InlineData("localhost", 0)]
    [InlineData("localhost", 65536)]
    public void InvalidEndpointIsRejected(string host, int port) => Assert.Throws<ArgumentException>(() => Probe.Validate(host, port));
    [Theory]
    [InlineData("SSH-2.0-OpenSSH\r\n", true)]
    [InlineData("notice\r\nSSH-2.0-OpenSSH\r\n", true)]
    [InlineData("HTTP/1.1 200 OK\r\n", false)]
    public void BannerMustBeSsh(string text, bool expected) => Assert.Equal(expected, Probe.HasBanner(text));
    [Fact] public async Task RealSocketReceivesSshBanner()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var port = ((IPEndPoint)server.LocalEndpoint).Port;
        var responder = Task.Run(async () =>
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = await server.AcceptTcpClientAsync(deadline.Token);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("SSH-2.0-TestServer\r\n"), deadline.Token);
        });
        await Probe.CheckAsync("127.0.0.1", port, TimeSpan.FromSeconds(5));
        await responder;
    }
    [Fact] public async Task SilentServerTimesOut()
    {
        using var server = new TcpListener(IPAddress.Loopback, 0);
        server.Start();
        var probe = Probe.CheckAsync("127.0.0.1", ((IPEndPoint)server.LocalEndpoint).Port, TimeSpan.FromMilliseconds(200));
        using var client = await server.AcceptTcpClientAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => probe);
    }
}
