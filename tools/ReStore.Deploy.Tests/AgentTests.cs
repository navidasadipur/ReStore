using System.Text.Json;
using ReStore.Deploy;
using Xunit;

namespace ReStore.Deploy.Tests;

public sealed class AgentTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "restore-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeHost host = new();
    public AgentTests()
    {
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "compose.yml"), "services: {}\n");
        File.WriteAllText(Path.Combine(root, ".env"), "RESTORE_TAG=production\n");
    }
    [Fact] public async Task DeploysOnlyAppAndWaitsForHealthBeforeApiCheck()
    {
        await new DeploymentAgent(root, host).RunAsync();
        Assert.Equal(4, host.Calls.Count);
        Assert.Equal("config", host.Calls[0][^3]);
        Assert.Equal(new[] { "pull", "app" }, host.Calls[1][^2..]);
        Assert.Equal(new[] { "up", "-d", "--no-deps", "--no-build", "--pull", "never",
            "--wait", "--wait-timeout", "180", "app" }, host.Calls[2][^10..]);
        Assert.Equal(new[] { "api-check" }, host.Calls[3]);
        Assert.False(Directory.Exists(Path.Combine(root, ".deploy")));
        Assert.False(File.Exists(Path.Combine(root, "compose.override.yml")));
    }
    [Fact] public async Task FailedPullNeverRunsComposeUp()
    {
        host.FailStep = "pull";
        await Assert.ThrowsAsync<IOException>(() => new DeploymentAgent(root, host).RunAsync());
        Assert.Equal(2, host.Calls.Count);
    }
    [Fact] public async Task FailedContainerHealthStopsWithoutRollback()
    {
        host.FailStep = "up";
        await Assert.ThrowsAsync<IOException>(() => new DeploymentAgent(root, host).RunAsync());
        Assert.Equal(3, host.Calls.Count);
    }
    [Fact] public async Task FailedApiCheckReportsFailureWithoutRollback()
    {
        host.FailStep = "api-check";
        await Assert.ThrowsAsync<IOException>(() => new DeploymentAgent(root, host).RunAsync());
        Assert.Equal(4, host.Calls.Count);
    }
    [Fact] public async Task WrongImageConfigIsRejectedBeforePull()
    {
        host.Image = "ghcr.io/navidasadipur/restore:latest";
        await Assert.ThrowsAsync<IOException>(() => new DeploymentAgent(root, host).RunAsync());
        Assert.Single(host.Calls);
    }
    [Theory]
    [InlineData("compose.override.yml")]
    [InlineData("compose.override.yaml")]
    public async Task ExistingOverridesAreNotIgnoredOrOverwritten(string name)
    {
        File.WriteAllText(Path.Combine(root, name), "custom");
        await Assert.ThrowsAsync<IOException>(() => new DeploymentAgent(root, host).RunAsync());
        Assert.Empty(host.Calls);
        Assert.Equal("custom", File.ReadAllText(Path.Combine(root, name)));
    }
    [Theory]
    [InlineData("init")]
    [InlineData("rollback")]
    [InlineData("check --image example")]
    [InlineData("check --directory")]
    public void RemovedOrInvalidOptionsAreRejected(string args) => Assert.Throws<ArgumentException>(
        () => Arguments.Parse(args.Split(' ')));
    [Fact] public void DefaultsAndDirectoryOptionAreSupported()
    {
        Assert.Equal("/opt/restore", Arguments.Parse([]).Directory);
        Assert.Equal(root, Arguments.Parse(["check", "--directory", root]).Directory);
    }
    public void Dispose()
    {
        var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (Path.GetDirectoryName(root) != temp || !Path.GetFileName(root).StartsWith("restore-tests-"))
            throw new IOException("Unexpected cleanup target");
        Directory.Delete(root, recursive: true);
    }
    private sealed class FakeHost : DeploymentHost
    {
        public List<string[]> Calls { get; } = [];
        public string Image { get; set; } = DeploymentAgent.ProductionImage;
        public string? FailStep { get; set; }
        public override Task<string> RunAsync(string directory, TimeSpan timeout, params string[] args)
        {
            Calls.Add(args);
            if (args.Contains(FailStep)) throw new IOException("Simulated " + FailStep + " failure");
            return Task.FromResult(args.Contains("config")
                ? JsonSerializer.Serialize(new { services = new { app = new { image = Image } } }) : "");
        }
        public override Task ProbePublicApiAsync()
        {
            Calls.Add(["api-check"]);
            if (FailStep == "api-check") throw new IOException("API failed");
            return Task.CompletedTask;
        }
    }
}
