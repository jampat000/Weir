namespace Weir.Tray.Tests;

/// <summary>
/// A fresh folder under the system temp directory, deleted on dispose. <see cref="AsWeirHome"/> also points
/// WEIR_HOME at it for the test's lifetime, so nothing a test logs or saves can reach a real install's runtime home.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    private const string WeirHomeVariable = Program.RuntimeHomeVariable;

    private readonly bool _isWeirHome;
    private readonly string? _previousHome;

    private TempDirectory(bool isWeirHome)
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "weir-tray-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
        _isWeirHome = isWeirHome;
        if (isWeirHome)
        {
            _previousHome = Environment.GetEnvironmentVariable(WeirHomeVariable);
            Environment.SetEnvironmentVariable(WeirHomeVariable, Path);
        }
    }

    public string Path { get; }

    public static TempDirectory Create() => new(isWeirHome: false);

    public static TempDirectory AsWeirHome() => new(isWeirHome: true);

    public void Dispose()
    {
        if (_isWeirHome)
        {
            Environment.SetEnvironmentVariable(WeirHomeVariable, _previousHome);
        }
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A process a test started may still hold a file for a moment; the folder is in temp and harmless.
        }
    }
}
