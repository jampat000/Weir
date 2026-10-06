using System.Reflection;

namespace Weir.Contract.Tests.Harness;

/// <summary>The Weir server executable the suite starts, and how to start it.</summary>
public sealed record ServerBinary(string Path)
{
    public const string OverrideVariable = "WEIR_CONTRACT_SERVER";
    private const string BuiltServerFolderKey = "BuiltServerFolder";
    private const string HostAssemblyFile = "Weir.dll";

    public static ServerBinary Locate()
    {
        var configured = Environment.GetEnvironmentVariable(OverrideVariable);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Existing(configured.Trim(), $"{OverrideVariable} points at {configured}, which does not exist.");
        }

        var folder = typeof(ServerBinary).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == BuiltServerFolderKey)
            .Value ?? throw new InvalidOperationException("The contract project did not record where the server is built.");
        var assembly = System.IO.Path.Combine(folder, HostAssemblyFile);
        return Existing(assembly, $"The server has not been built: {assembly} does not exist. Build apps/server first.");
    }

    public string WorkingFolder => System.IO.Path.GetDirectoryName(Path)!;

    /// <summary>The program and leading arguments that run the server: <c>dotnet Weir.dll</c>, or a published executable on its own.</summary>
    public (string Program, IReadOnlyList<string> Arguments) Command() =>
        Path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? ("dotnet", [Path]) : (Path, []);

    private static ServerBinary Existing(string path, string missingMessage) =>
        File.Exists(path) ? new ServerBinary(path) : throw new FileNotFoundException(missingMessage);
}
