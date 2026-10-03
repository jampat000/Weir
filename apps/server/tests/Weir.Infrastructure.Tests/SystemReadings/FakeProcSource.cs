using Weir.Infrastructure.SystemReadings;

namespace Weir.Infrastructure.Tests.SystemReadings;

/// <summary>The files of a Linux machine, as the test lays them out.</summary>
internal sealed class FakeProcSource : IProcSource
{
    private readonly Dictionary<string, string> _files = [];

    public FakeProcSource With(string path, string contents)
    {
        _files[path] = contents;
        return this;
    }

    public string? ReadAllText(string path) => _files.GetValueOrDefault(path);

    public bool Exists(string path) =>
        _files.ContainsKey(path) || _files.Keys.Any(file => file.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal));
}
