using System.Diagnostics;
using System.Net;
using System.Text.Json;

namespace ReStore.Deploy;

public sealed record Arguments(string Directory)
{
    public static Arguments Parse(string[] args)
    {
        var directory = "/opt/restore";
        var index = args.Length > 0 && args[0] == "check" ? 1 : 0;
        if (index < args.Length)
        {
            if (args.Length != index + 2 || args[index] != "--directory" || string.IsNullOrWhiteSpace(args[index + 1]))
                throw new ArgumentException("Usage: restore-deploy [check] [--directory /opt/restore]");
            directory = args[index + 1];
        }
        return new(directory);
    }
}

public class DeploymentHost
{
    public virtual async Task<string> RunAsync(string directory, TimeSpan timeout, params string[] args)
    {
        var start = new ProcessStartInfo(args[0])
        {
            WorkingDirectory = directory, RedirectStandardOutput = true,
            RedirectStandardError = true, UseShellExecute = false
        };
        foreach (var argument in args.Skip(1)) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start " + args[0]);
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
            throw new IOException("Command timed out: " + string.Join(' ', args.Take(3)));
        }
        await Task.WhenAll(stdout, stderr);
        // Do not include command output: Compose diagnostics may contain secrets.
        if (process.ExitCode != 0)
            throw new IOException($"Command failed (exit {process.ExitCode}): {string.Join(' ', args.Take(3))}");
        return (await stdout).Trim();
    }

    public virtual async Task ProbePublicApiAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1024 * 1024 };
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "https://restore.navidasadipur.ir/api/products?pageSize=1&deploy_probe=" + Guid.NewGuid().ToString("N"));
        request.Headers.CacheControl = new() { NoCache = true };
        using var response = await client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK) throw new IOException("Public API did not return HTTP 200");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (body.RootElement.ValueKind != JsonValueKind.Array) throw new IOException("Public API did not return a product array");
    }
}
