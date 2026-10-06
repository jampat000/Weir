namespace Weir.Contract.Tests.Processing;

/// <summary>The three folders a workflow works in, created under one root.</summary>
internal sealed record Folders(string Watched, string Work, string Output)
{
    public static Folders Make(string root)
    {
        var folders = new Folders(Path.Combine(root, "watched"), Path.Combine(root, "work"), Path.Combine(root, "output"));
        Directory.CreateDirectory(folders.Watched);
        Directory.CreateDirectory(folders.Work);
        Directory.CreateDirectory(folders.Output);
        return folders;
    }
}
