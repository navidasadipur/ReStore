using ReStore.Deploy;

try
{
    var options = Arguments.Parse(args);
    var agent = new DeploymentAgent(options.Directory);
    await agent.RunAsync();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{DateTimeOffset.UtcNow:O} ERROR: {ex.Message}");
    return 1;
}
