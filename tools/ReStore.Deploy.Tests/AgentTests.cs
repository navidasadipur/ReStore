using System.Text.Json;
using ReStore.Deploy;
using Xunit;

namespace ReStore.Deploy.Tests;

public class AgentTests : IDisposable
{
    public const string Old = DeploymentAgent.Image + "@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string New = DeploymentAgent.Image + "@sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly string root = Path.Combine(Path.GetTempPath(), "restore-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeAgent agent;
    public AgentTests()
    {
        Directory.CreateDirectory(Path.Combine(root, ".deploy"));
        File.WriteAllText(Path.Combine(root, "compose.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, ".env"), "SECRET=not-for-logs\n");
        agent = new(root);
    }
    private async Task ReadyAsync()
    {
        await agent.InitializeAsync(Old);
        await agent.ActAsync("resume");
        agent.Calls.Clear();
    }
    [Fact] public async Task InitializationPinsBaselineWithoutRestartingAndStaysPaused()
    {
        await agent.InitializeAsync(Old);
        Assert.True(agent.Load().Paused);
        Assert.Contains(Old, File.ReadAllText(agent.OverridePath));
        Assert.Empty(agent.UpCalls);
    }
    [Fact] public async Task SuccessfulReleaseRecordsPreviousAndOnlyRecreatesApp()
    {
        await ReadyAsync();
        await agent.ActAsync("check");
        var state = agent.Load();
        Assert.Equal(New, state.Current);
        Assert.Equal(Old, state.Previous);
        Assert.Null(state.Pending);
        Assert.Contains(New, File.ReadAllText(agent.OverridePath));
        var up = Assert.Single(agent.UpCalls);
        Assert.Contains("--no-deps", up);
        Assert.Contains("--no-build", up);
        Assert.Equal("app", up[^1]);
    }
    [Fact] public async Task UnchangedReleaseDoesNotPullOrRestart()
    {
        await ReadyAsync(); agent.Channel = Old;
        await agent.ActAsync("check");
        Assert.Empty(agent.UpCalls);
        Assert.DoesNotContain(agent.Calls, call => call.Take(2).SequenceEqual(["docker", "pull"]));
    }
    [Fact] public async Task RegistryFailureLeavesAppUnchangedAndRecordsError()
    {
        await ReadyAsync(); agent.Channel = null;
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.Equal(Old, agent.Load().Current);
        Assert.Contains(Old, File.ReadAllText(agent.OverridePath));
        Assert.Empty(agent.UpCalls);
        Assert.Contains("Registry", agent.Load().LastError);
    }
    [Fact] public async Task DownloadFailureDoesNotStartTransaction()
    {
        await ReadyAsync(); agent.FailPull = true;
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.Null(agent.Load().Pending);
        Assert.Contains(Old, File.ReadAllText(agent.OverridePath));
        Assert.Empty(agent.UpCalls);
    }
    [Fact] public async Task FailedHealthRollsBackAndDoesNotRetryBadDigest()
    {
        await ReadyAsync(); agent.BadHealth.Add(New);
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.Equal(Old, agent.Load().Current);
        Assert.Null(agent.Load().Pending);
        Assert.Contains(New, agent.Load().Failed);
        Assert.Equal(2, agent.UpCalls.Count());
        agent.Calls.Clear();
        await agent.ActAsync("check");
        Assert.Empty(agent.UpCalls);
    }
    [Fact] public async Task ComposeFailureRollsBack()
    {
        await ReadyAsync(); agent.FailUp.Add(New);
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.Equal(Old, agent.Load().Current);
        Assert.Null(agent.Load().Pending);
        Assert.Contains(Old, File.ReadAllText(agent.OverridePath));
    }
    [Fact] public async Task FailedRollbackPausesAndRetainsRecoveryJournal()
    {
        await ReadyAsync(); agent.BadHealth.Add(New); agent.FailUp.Add(Old);
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.True(agent.Load().Paused);
        Assert.Equal(New, agent.Load().Pending);
        Assert.Contains(New, agent.Load().Failed);
    }
    [Fact] public async Task RestartRecoversPendingDeploymentAndRemainsPaused()
    {
        await ReadyAsync();
        var state = agent.Load(); state.Pending = New;
        agent.Save(state); agent.Pin(New);
        var restarted = new FakeAgent(root);
        await restarted.ActAsync("check");
        Assert.True(restarted.Load().Paused);
        Assert.Null(restarted.Load().Pending);
        Assert.Equal(Old, restarted.Load().Current);
        Assert.Contains(Old, File.ReadAllText(restarted.OverridePath));
        Assert.DoesNotContain(restarted.Calls, call => call[0] == "resolve");
    }
    [Fact] public async Task ManualRollbackUsesLocalImageAndPauses()
    {
        await ReadyAsync(); await agent.ActAsync("check");
        agent.Calls.Clear(); agent.FailPull = true;
        await agent.ActAsync("rollback");
        Assert.Equal(Old, agent.Load().Current);
        Assert.True(agent.Load().Paused);
        Assert.DoesNotContain(agent.Calls, call => call.Take(2).SequenceEqual(["docker", "pull"]));
    }
    [Fact] public async Task PausePreventsRegistryAccess()
    {
        await ReadyAsync(); await agent.ActAsync("pause"); agent.Calls.Clear();
        await agent.ActAsync("check");
        Assert.DoesNotContain(agent.Calls, call => call[0] == "resolve");
        Assert.Empty(agent.UpCalls);
    }
    [Fact] public async Task UnmanagedOverrideIsNotOverwritten()
    {
        await ReadyAsync(); File.WriteAllText(agent.OverridePath, "custom");
        await Assert.ThrowsAsync<IOException>(() => agent.ActAsync("check"));
        Assert.Equal("custom", File.ReadAllText(agent.OverridePath));
    }
    [Fact] public void OtherRepositoryAndMutableTagAreRejected()
    {
        Assert.Throws<ArgumentException>(() => agent.Pin("other/repo@sha256:" + new string('a', 64)));
        Assert.Throws<ArgumentException>(() => agent.Pin(DeploymentAgent.Image + ":production"));
    }
    [Fact] public void LockExcludesAnotherControlCommand()
    {
        using var held = DeploymentLock.Acquire(agent.DataDirectory);
        Assert.Throws<IOException>(() => DeploymentLock.Acquire(agent.DataDirectory));
    }
    [Fact] public async Task ResolveUsesTopLevelManifestDigest()
    {
        var host = new ProbeHost { Manifest = "{\"digest\":\"sha256:" + new string('b', 64) + "\"}" };
        var real = new DeploymentAgent(root, host);
        Assert.Equal(New, await real.ResolveAsync());
        Assert.Contains("{{json .Manifest}}", Assert.Single(host.Calls));
    }
    [Theory]
    [InlineData("wrong-image", false, false)]
    [InlineData("expected", true, false)]
    [InlineData("expected", false, true)]
    public async Task HealthRequiresMatchingImageAndDatabaseApi(string runningId, bool databaseFails, bool succeeds)
    {
        var host = new ProbeHost { RunningId = runningId, DatabaseFails = databaseFails };
        var real = new DeploymentAgent(root, host);
        if (succeeds) await real.HealthyAsync(New, 0);
        else await Assert.ThrowsAsync<IOException>(() => real.HealthyAsync(New, 0));
        Assert.Equal(succeeds ? 1 : 0, host.PublicChecks);
    }
    [Fact] public async Task PublicApiFailureDoesNotPassHealthGate()
    {
        var real = new DeploymentAgent(root, new ProbeHost { PublicFails = true });
        await Assert.ThrowsAsync<IOException>(() => real.HealthyAsync(New, 0));
    }
    [Theory]
    [InlineData("")]
    [InlineData("init")]
    [InlineData("check --unknown x")]
    public void InvalidArgumentsFail(string args) => Assert.Throws<ArgumentException>(() => Arguments.Parse(args.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
    public void Dispose()
    {
        // Only remove this fixture's randomly named directory directly under temp.
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(root) != temp || !Path.GetFileName(root).StartsWith("restore-tests-"))
            throw new IOException("Unexpected cleanup target");
        Directory.Delete(root, recursive: true);
    }

    private sealed class FakeAgent(string directory) : DeploymentAgent(directory)
    {
        public List<string[]> Calls { get; } = [];
        public HashSet<string> BadHealth { get; } = [];
        public HashSet<string> FailUp { get; } = [];
        public bool FailPull { get; set; }
        public string? Channel { get; set; } = New;
        public IEnumerable<string[]> UpCalls => Calls.Where(call => call.Take(2).SequenceEqual(["compose", "up"]));
        public override Task<string> CommandAsync(int seconds, params string[] args)
        {
            Calls.Add(args);
            if (args.Take(2).SequenceEqual(["docker", "pull"]) && FailPull) throw new IOException("Registry unavailable");
            return Task.FromResult("image-id");
        }
        public override Task<string> ComposeAsync(params string[] args)
        {
            Calls.Add(["compose", .. args]);
            if (args[0] == "up" && FailUp.Any(image => File.ReadAllText(OverridePath).Contains(image)))
                throw new IOException("Compose failed");
            return Task.FromResult("");
        }
        public override Task HealthyAsync(string image, int seconds = 180)
        {
            if (BadHealth.Contains(image)) throw new IOException("Health failed");
            return Task.CompletedTask;
        }
        public override Task<string> ResolveAsync()
        {
            Calls.Add(["resolve"]);
            return Task.FromResult(Channel ?? throw new IOException("Registry unavailable"));
        }
    }
    private sealed class ProbeHost : DeploymentHost
    {
        public List<string[]> Calls { get; } = [];
        public string Manifest { get; set; } = "{}";
        public string RunningId { get; set; } = "expected";
        public bool DatabaseFails { get; set; }
        public bool PublicFails { get; set; }
        public int PublicChecks { get; private set; }
        public override Task<string> RunAsync(string directory, TimeSpan timeout, params string[] args)
        {
            Calls.Add(args);
            if (args.Take(3).SequenceEqual(["docker", "buildx", "imagetools"])) return Task.FromResult(Manifest);
            if (args.Take(3).SequenceEqual(["docker", "image", "inspect"])) return Task.FromResult("expected");
            if (args.Take(2).SequenceEqual(["docker", "inspect"]))
                return Task.FromResult(JsonSerializer.Serialize(new[] { new { Image = RunningId,
                    State = new { Running = true, Health = new { Status = "healthy" } } } }));
            if (args.Take(2).SequenceEqual(["docker", "exec"]))
            {
                Assert.Contains("/api/products", args[^1]);
                if (DatabaseFails) throw new IOException("Database returned HTTP 500");
                return Task.FromResult("");
            }
            throw new IOException("Unexpected command");
        }
        public override Task ProbePublicApiAsync()
        {
            if (PublicFails) throw new IOException("Public API failed");
            PublicChecks++;
            return Task.CompletedTask;
        }
    }
}
