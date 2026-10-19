using System.Text.Json;

namespace ReStore.Deploy;

public sealed class DeploymentAgent(string directory, DeploymentHost? host = null)
{
    public const string ProductionImage = "ghcr.io/navidasadipur/restore:production";
    public string Root { get; } = Path.GetFullPath(directory);
    private readonly DeploymentHost host = host ?? new DeploymentHost();

    public async Task RunAsync()
    {
        foreach (var name in new[] { "compose.yml", ".env" })
            if (!File.Exists(Path.Combine(Root, name))) throw new IOException("Missing " + name);
        // Keep the agent and ordinary manual Compose commands on the same config.
        foreach (var name in new[] { "compose.override.yml", "compose.override.yaml",
                     "docker-compose.override.yml", "docker-compose.override.yaml" })
            if (File.Exists(Path.Combine(Root, name)))
                throw new IOException("Review existing override before deployment: " + name);

        using var config = JsonDocument.Parse(await ComposeAsync(30, "config", "--format", "json"));
        var image = config.RootElement.GetProperty("services").GetProperty("app").GetProperty("image").GetString();
        if (image != ProductionImage)
            throw new IOException("Set RESTORE_IMAGE=ghcr.io/navidasadipur/restore and RESTORE_TAG=production in .env before using the agent");

        Log("Pulling " + ProductionImage);
        // A failed pull exits before any container update.
        await ComposeAsync(300, "pull", "app");
        Log("Updating app and waiting for container health");
        // Compose reuses an unchanged container. --no-deps isolates this update
        // to app; --pull never uses the image downloaded by the preceding step.
        await ComposeAsync(240, "up", "-d", "--no-deps", "--no-build", "--pull", "never",
            "--wait", "--wait-timeout", "180", "app");
        // The existing container healthcheck only probes its listening socket.
        // This endpoint also queries the database and checks the HTTPS route.
        Log("Checking the public products API");
        await host.ProbePublicApiAsync();
        Log("Deployment health check passed.");
    }

    private Task<string> ComposeAsync(int seconds, params string[] args) => host.RunAsync(Root,
        TimeSpan.FromSeconds(seconds),
        ["docker", "compose", "--project-directory", Root, "-f", Path.Combine(Root, "compose.yml"), .. args]);

    private static void Log(string message) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} {message}");
}
