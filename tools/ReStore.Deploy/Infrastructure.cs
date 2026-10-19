using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ReStore.Deploy;

public sealed record Arguments(string Action, string Directory, string? Image)
{
    public static Arguments Parse(string[] args)
    {
        if (args.Length == 0 || !new[] { "init", "check", "status", "pause", "resume", "rollback" }.Contains(args[0]))
            throw new ArgumentException("Usage: restore-deploy init|check|status|pause|resume|rollback [--directory /opt/restore] [--image IMAGE@sha256:digest]");
        var directory = "/opt/restore";
        string? image = null;
        for (var i = 1; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length) throw new ArgumentException("Missing option value");
            switch (args[i])
            {
                case "--directory": directory = args[i + 1]; break;
                case "--image": image = args[i + 1]; break;
                default: throw new ArgumentException("Unknown option: " + args[i]);
            }
        }
        if (args[0] == "init" && image is null) throw new ArgumentException("init requires --image");
        return new(args[0], directory, image);
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
        start.Environment["BUILDX_CONFIG"] = Path.Combine(directory, ".deploy", "buildx");
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

internal static class Native
{
    [DllImport("libc", SetLastError = true)] internal static extern int flock(int fd, int operation);
    [DllImport("libc", SetLastError = true)] internal static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] internal static extern int fsync(int fd);
    [DllImport("libc", SetLastError = true)] internal static extern int close(int fd);
}

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        if (new FileInfo(path).LinkTarget is not null) throw new IOException("Refusing to replace a symbolic link: " + path);
        var directory = Path.GetDirectoryName(path)!;
        var temporary = Path.Combine(directory, ".restore-" + Guid.NewGuid().ToString("N"));
        try
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            using (var file = new FileStream(temporary, options))
            {
                file.Write(Encoding.UTF8.GetBytes(content));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
            if (OperatingSystem.IsLinux())
            {
                var fd = Native.open(directory, 0); // O_RDONLY; fsync the containing directory after rename.
                if (fd < 0) throw new IOException("Could not open state directory for durability");
                try { if (Native.fsync(fd) != 0) throw new IOException("Could not flush state directory"); }
                finally { Native.close(fd); }
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class DeploymentLock : IDisposable
{
    private readonly FileStream file;
    private DeploymentLock(FileStream file) => this.file = file;
    public static DeploymentLock Acquire(string directory)
    {
        if (OperatingSystem.IsWindows()) System.IO.Directory.CreateDirectory(directory);
        else System.IO.Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var file = new FileStream(Path.Combine(directory, "lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, OperatingSystem.IsWindows() ? FileShare.None : FileShare.ReadWrite);
        if (OperatingSystem.IsLinux() && Native.flock(file.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
        {
            file.Dispose();
            throw new IOException("Another deployment/control command is running");
        }
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            file.Dispose();
            throw new PlatformNotSupportedException("Deployment locking requires Linux or Windows");
        }
        return new(file);
    }
    public void Dispose() => file.Dispose();
}
