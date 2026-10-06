using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Weir.LiveAudit;

/// <summary>
/// The folders and the FFmpeg-made media file the pass-through proof runs on. The audit writes them where the
/// server can see them: the host root is the path the audit uses, the server root is the same
/// folder as the server sees it (the same path for a local server, a mount point for a Docker one).
/// </summary>
internal sealed partial class PassThroughFixture
{
    public const string ReleaseFolder = "ForeignFilm";
    public const string FileName = "foreign-only.mkv";

    private readonly string name;
    private readonly string serverRoot;
    private readonly char serverSeparator;

    private PassThroughFixture(string hostRoot, string serverRoot)
    {
        name = $"pass-through-{Guid.NewGuid():N}";
        this.serverRoot = serverRoot;
        serverSeparator = WindowsDrivePath().IsMatch(serverRoot) ? '\\' : '/';
        Root = Path.Combine(hostRoot, name);
        Watch = Path.Combine(Root, "watch");
        Work = Path.Combine(Root, "work");
        Output = Path.Combine(Root, "processed");
        Source = Path.Combine(Watch, ReleaseFolder, FileName);
        Delivered = Path.Combine(Output, ReleaseFolder, FileName);
    }

    public string Root { get; }
    public string Watch { get; }
    public string Work { get; }
    public string Output { get; }

    /// <summary>The file the server is asked to pass through.</summary>
    public string Source { get; }

    /// <summary>Where the unchanged file must arrive.</summary>
    public string Delivered { get; }

    public static PassThroughFixture Create(string hostRoot, string serverRoot)
    {
        var resolvedRoot = Path.GetFullPath(ExpandHome(hostRoot));
        Directory.CreateDirectory(resolvedRoot);
        var fixture = new PassThroughFixture(resolvedRoot, serverRoot);
        // Each folder must be new: a leftover from an earlier run would make the proof prove nothing.
        foreach (var directory in new[] { fixture.Root, fixture.Watch, fixture.Work, fixture.Output, Path.GetDirectoryName(fixture.Source)! })
        {
            if (Directory.Exists(directory))
            {
                throw new IOException($"Fixture folder already exists: {directory}");
            }

            Directory.CreateDirectory(directory);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
            }
        }

        return fixture;
    }

    /// <summary>The path of a fixture folder as the server sees it.</summary>
    public string ServerPath(params string[] parts) =>
        serverRoot.TrimEnd('/', '\\') + serverSeparator + string.Join(serverSeparator, parts);

    public string ServerFolder(string folder) => ServerPath(name, folder);

    /// <summary>Makes a two-second mpeg4 and AAC file whose audio is tagged Japanese.</summary>
    public async Task CreateSourceAsync(string ffmpeg)
    {
        var info = new ProcessStartInfo(ffmpeg) { UseShellExecute = false };
        foreach (var argument in new[]
        {
            "-nostdin", "-hide_banner", "-loglevel", "error",
            "-f", "lavfi", "-i", "color=c=black:s=320x180:d=2",
            "-f", "lavfi", "-i", "sine=frequency=440:duration=2",
            "-map", "0:v", "-map", "1:a",
            "-c:v", "mpeg4", "-c:a", "aac",
            "-metadata:s:a:0", "language=jpn",
            "-y", Source,
        })
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {ffmpeg}.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await process.WaitForExitAsync(timeout.Token);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg returned non-zero exit status {process.ExitCode}.");
        }
    }

    public static string Sha256(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));

    private static string ExpandHome(string path) =>
        path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith("~\\", StringComparison.Ordinal)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.TrimStart('~', '/', '\\'))
            : path;

    [GeneratedRegex(@"^[A-Za-z]:[\\/]")]
    private static partial Regex WindowsDrivePath();
}
