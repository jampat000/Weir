using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Weir.LiveAudit;

/// <summary>
/// Reads the one-time setup code the way a remote operator is told to: from the container's own log line, or the
/// setup-code file in its data folder if the log has since rotated past it.
/// </summary>
internal static partial class DockerSetupCode
{
    public static async Task<string> ReadAsync(AuditConfig config)
    {
        var container = config.DockerContainer;
        var logs = await RunAsync("docker", "logs", container);
        var match = SetupCodePattern().Match(logs);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        var code = (await RunAsync("docker", "exec", container, "cat", $"{config.DockerWeirHome}/setup-code")).Trim();
        if (code.Length == 0)
        {
            throw new InvalidOperationException(
                $"Found no setup code in {container}'s log or {config.DockerWeirHome}/setup-code.");
        }

        return code;
    }

    /// <summary>Runs a command and returns its stdout and stderr together; a non-zero exit is an error.</summary>
    private static async Task<string> RunAsync(string fileName, params string[] arguments)
    {
        var info = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException($"Could not start {fileName}.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        var text = await output + await error;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {string.Join(' ', arguments)}' returned non-zero exit status {process.ExitCode}.");
        }

        return text;
    }

    [GeneratedRegex(@"enter this setup code:\s*([A-Z0-9]{4}-[A-Z0-9]{4})")]
    private static partial Regex SetupCodePattern();
}
