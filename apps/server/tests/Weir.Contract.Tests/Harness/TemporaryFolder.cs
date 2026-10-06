namespace Weir.Contract.Tests.Harness;

/// <summary>A folder that exists for one test and is deleted with it.</summary>
public sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder() => Path = Directory.CreateTempSubdirectory("weir_contract_files_").FullName;

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
