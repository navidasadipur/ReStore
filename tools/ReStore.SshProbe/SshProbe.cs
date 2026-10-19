using System.Net.Sockets;
using System.Text;

namespace ReStore.SshProbe;

public static class SshProbe
{
    public static void Validate(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || host.StartsWith('-') || host.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter a plain server IP or hostname");
        if (port is < 1 or > 65535) throw new ArgumentException("SSH port must be between 1 and 65535");
    }
    public static bool HasBanner(string response) => response.Split('\n').Any(line => line.StartsWith("SSH-", StringComparison.Ordinal));
    public static async Task CheckAsync(string host, int port, TimeSpan timeout)
    {
        Validate(host, port);
        using var client = new TcpClient();
        using (var connect = new CancellationTokenSource(timeout))
            await client.ConnectAsync(host, port, connect.Token);
        using var exchange = new CancellationTokenSource(timeout);
        using var stream = client.GetStream();
        await stream.WriteAsync(Encoding.ASCII.GetBytes("SSH-2.0-ReStoreConnectivityTest\r\n"), exchange.Token);
        var buffer = new byte[8192];
        var received = 0;
        while (received < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(received), exchange.Token);
            if (count == 0) break;
            received += count;
            if (HasBanner(Encoding.ASCII.GetString(buffer, 0, received))) return;
        }
        throw new IOException("TCP connected, but no SSH protocol response received");
    }
}
