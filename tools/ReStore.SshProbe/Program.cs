using ReStore.SshProbe;

try
{
    var host = Environment.GetEnvironmentVariable("SSH_HOST")?.Trim() ?? "";
    if (!int.TryParse(Environment.GetEnvironmentVariable("SSH_PORT"), out var port))
        throw new ArgumentException("SSH_PORT must be an integer");
    SshProbe.Validate(host, port);
    var passed = 0;
    for (var attempt = 1; attempt <= 3; attempt++)
    {
        try
        {
            await SshProbe.CheckAsync(host, port, TimeSpan.FromSeconds(10));
            passed++;
            Console.WriteLine($"Attempt {attempt}: SSH protocol response received.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Attempt {attempt}: {ex.GetType().Name}: {ex.Message}");
        }
    }
    var summary = $"### SSH network test\n\nSSH responses: {passed}/3 from a GitHub-hosted runner.\n\n" +
        "This tests network reachability only; it does not authenticate or deploy.\n";
    if (Environment.GetEnvironmentVariable("GITHUB_STEP_SUMMARY") is { Length: > 0 } path)
        await File.AppendAllTextAsync(path, summary);
    if (passed != 3) throw new IOException("Network test failed or was intermittent. Inspect the attempts above.");
    return 0;
}
catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 1; }
