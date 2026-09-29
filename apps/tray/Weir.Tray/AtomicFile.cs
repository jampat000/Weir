namespace Weir.Tray;

/// <summary>
/// Saves a small settings file so a reader sees the old contents or the new ones, never half of either. The text is
/// written to a scratch file first and renamed into place; a plain <c>File.WriteAllText</c> truncates before it
/// writes, so a crash mid-write would leave the torn file every loader then has to guess its way around.
/// </summary>
static class AtomicFile
{
    /// <summary>
    /// Replaces <paramref name="fileName"/> in <paramref name="directory"/>, creating the folder if needed. The
    /// scratch name is unique per write, because the server writes some of these files too: a shared scratch name
    /// would let one writer rename the other's file into place. It sits in the same folder so the rename stays
    /// atomic, and starts with a dot so a leftover is not mistaken for a real file.
    /// </summary>
    internal static void WriteAllText(string directory, string fileName, string text)
    {
        Directory.CreateDirectory(directory);
        var scratch = Path.Combine(directory, $".{fileName}.{Guid.NewGuid():n}.tmp");
        try
        {
            File.WriteAllText(scratch, text);
            File.Move(scratch, Path.Combine(directory, fileName), overwrite: true);
        }
        finally
        {
            // Only still there when the write or the rename failed.
            File.Delete(scratch);
        }
    }
}
