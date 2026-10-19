using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace ReStore.Deploy;

public sealed class DeploymentState
{
    public required string Current { get; set; }
    public string? Previous { get; set; }
    public string? Pending { get; set; }
    public List<string> Failed { get; set; } = [];
    public bool Paused { get; set; } = true;
    public string? LastError { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class DeploymentAgent
{
    public const string Image = "ghcr.io/navidasadipur/restore";
    public const string Marker = "# Managed by restore-deploy; do not edit.\n";
    public const string ApiProbe = "exec 3<>/dev/tcp/127.0.0.1/8080; printf 'GET /api/products?pageSize=1 HTTP/1.0\\r\\nHost: localhost\\r\\nConnection: close\\r\\n\\r\\n' >&3; IFS= read -r status <&3; [[ \"$status\" == *\" 200 \"* ]]";
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    public string Root { get; }
    public string DataDirectory => Path.Combine(Root, ".deploy");
    public string StatePath => Path.Combine(DataDirectory, "state.json");
    public string OverridePath => Path.Combine(Root, "compose.override.yml");
    protected DeploymentHost Host { get; }
    public DeploymentAgent(string directory, DeploymentHost? host = null)
    {
        Root = Path.GetFullPath(directory);
        Host = host ?? new DeploymentHost();
    }
    protected static void Log(string text) => Console.WriteLine($"{DateTimeOffset.UtcNow:O} {text}");
    public static string Reference(string value) => Regex.IsMatch(value,
        "\\A" + Regex.Escape(Image) + "@sha256:[0-9a-f]{64}\\z")
        ? value : throw new ArgumentException("Expected a ReStore image reference pinned to a sha256 digest");
    public virtual Task<string> CommandAsync(int seconds, params string[] args) => Host.RunAsync(Root, TimeSpan.FromSeconds(seconds), args);
    public virtual Task<string> ComposeAsync(params string[] args) => CommandAsync(120,
        ["docker", "compose", "--project-directory", Root, "-f", Path.Combine(Root, "compose.yml"), "-f", OverridePath, .. args]);
    public async Task PreflightAsync()
    {
        foreach (var name in new[] { "compose.yml", ".env" })
            if (!File.Exists(Path.Combine(Root, name))) throw new IOException("Missing " + name);
        foreach (var name in new[] { "compose.override.yaml", "docker-compose.override.yml", "docker-compose.override.yaml" })
            if (File.Exists(Path.Combine(Root, name))) throw new IOException("Review existing override before installation: " + name);
        if (File.Exists(OverridePath) && !File.ReadAllText(OverridePath).StartsWith(Marker, StringComparison.Ordinal))
            throw new IOException("Refusing to overwrite an unmanaged compose.override.yml");
        await CommandAsync(20, "docker", "info");
        await CommandAsync(20, "docker", "compose", "version");
        await CommandAsync(20, "docker", "buildx", "version");
    }
    public DeploymentState Load()
    {
        if (!File.Exists(StatePath)) throw new IOException("Initialize the running baseline with init --image first");
        var state = JsonSerializer.Deserialize<DeploymentState>(File.ReadAllText(StatePath), JsonOptions)
            ?? throw new IOException("Invalid deployment state");
        Reference(state.Current);
        if (state.Previous is not null) Reference(state.Previous);
        if (state.Pending is not null) Reference(state.Pending);
        if (state.Failed is null) throw new IOException("Invalid failed-digest list");
        foreach (var failed in state.Failed) Reference(failed);
        return state;
    }
    public void Save(DeploymentState state)
    {
        state.UpdatedAt = DateTimeOffset.UtcNow;
        AtomicFile.Write(StatePath, JsonSerializer.Serialize(state, JsonOptions) + "\n");
    }
    public void Pin(string image) => AtomicFile.Write(OverridePath,
        Marker + "services:\n  app:\n    image: " + JsonSerializer.Serialize(Reference(image)) + "\n");
    public Task<string> ImageIdAsync(string image) => CommandAsync(20, "docker", "image", "inspect", Reference(image), "--format", "{{.Id}}");
    public virtual async Task HealthyAsync(string image, int seconds = 180)
    {
        var expected = await ImageIdAsync(image);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var document = JsonDocument.Parse(await CommandAsync(20, "docker", "inspect", "restore-app"));
                var app = document.RootElement[0];
                var state = app.GetProperty("State");
                if (app.GetProperty("Image").GetString() == expected && state.GetProperty("Running").GetBoolean() &&
                    state.TryGetProperty("Health", out var health) && health.GetProperty("Status").GetString() == "healthy")
                {
                    await CommandAsync(15, "docker", "exec", "restore-app", "timeout", "10", "bash", "-c", ApiProbe);
                    await Host.ProbePublicApiAsync();
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or HttpRequestException or TaskCanceledException) { }
            if (watch.Elapsed >= TimeSpan.FromSeconds(seconds)) throw new IOException("Health gate failed for " + image);
            await Task.Delay(TimeSpan.FromSeconds(5));
        }
    }
    public virtual async Task<string> ResolveAsync()
    {
        using var manifest = JsonDocument.Parse(await CommandAsync(60, "docker", "buildx", "imagetools", "inspect",
            Image + ":production", "--format", "{{json .Manifest}}"));
        return Reference(Image + "@" + manifest.RootElement.GetProperty("digest").GetString());
    }
    public async Task InitializeAsync(string image)
    {
        Reference(image);
        if (File.Exists(StatePath) || File.Exists(OverridePath)) throw new IOException("Already initialized or override exists; inspect status first");
        await HealthyAsync(image, 0);
        Pin(image);
        Save(new() { Current = image });
        Log("Initialized and paused; baseline pinned without restarting the app");
    }
    public async Task RestoreAsync(DeploymentState state, string failed)
    {
        if (!state.Failed.Contains(failed)) state.Failed.Add(failed);
        Save(state);
        Pin(state.Current);
        await ComposeAsync("config", "--quiet");
        await ComposeAsync("up", "-d", "--no-deps", "--no-build", "--pull", "never", "app");
        await HealthyAsync(state.Current);
        state.Pending = null;
        Save(state);
        Log("Previous version restored and verified");
    }
    public async Task RecoverAsync(DeploymentState state)
    {
        if (state.Pending is null) return;
        state.Paused = true;
        state.LastError = "Interrupted deployment; recovering previous version";
        Save(state);
        await RestoreAsync(state, state.Pending);
        Log("Recovery completed; automatic deployment remains paused");
    }
    public async Task DeployAsync(DeploymentState state, string candidate, bool pull = true)
    {
        Reference(candidate);
        await HealthyAsync(state.Current, 0);
        if (pull) await CommandAsync(300, "docker", "pull", candidate);
        await ImageIdAsync(candidate);
        state.Pending = candidate;
        Save(state);
        try
        {
            Pin(candidate);
            await ComposeAsync("config", "--quiet");
            await ComposeAsync("up", "-d", "--no-deps", "--no-build", "--pull", "never", "app");
            await HealthyAsync(candidate);
        }
        catch (Exception ex)
        {
            state.LastError = ex.Message;
            Save(state);
            try { await RestoreAsync(state, candidate); }
            catch
            {
                state.Paused = true;
                Save(state);
                throw new IOException("Deployment and rollback failed; paused with recovery pending");
            }
            throw new IOException("Deployment failed; verified previous version restored", ex);
        }
        state.Previous = state.Current;
        state.Current = candidate;
        state.Pending = null;
        state.LastError = null;
        Save(state);
        Log("Deployment verified: " + candidate);
    }
    public async Task ActAsync(string action, string? image = null)
    {
        await PreflightAsync();
        if (action == "init") { await InitializeAsync(image ?? throw new ArgumentException("init requires --image")); return; }
        var state = Load();
        if (action == "status") { Console.WriteLine(JsonSerializer.Serialize(state, JsonOptions)); return; }
        if (action == "pause") { state.Paused = true; Save(state); Log("Automatic deployment paused"); return; }
        await RecoverAsync(state);
        switch (action)
        {
            case "resume":
                await HealthyAsync(state.Current, 0);
                state.Paused = false;
                Save(state);
                Log("Automatic deployment enabled; failed digests remain blocked");
                break;
            case "rollback":
                if (state.Previous is null) throw new IOException("No previous successful version is recorded");
                state.Paused = true;
                Save(state);
                await DeployAsync(state, state.Previous, pull: false);
                break;
            case "check":
                if (state.Paused) { Log("Paused; no registry check or deployment"); return; }
                try
                {
                    var candidate = await ResolveAsync();
                    if (candidate == state.Current) Log("Already running production; no restart");
                    else if (state.Failed.Contains(candidate)) Log("Production digest previously failed; leaving app unchanged");
                    else await DeployAsync(state, candidate);
                }
                catch (Exception ex) { state.LastError = ex.Message; Save(state); throw; }
                break;
            default: throw new ArgumentException("Unknown action: " + action);
        }
    }
}
