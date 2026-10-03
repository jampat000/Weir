using System.Security.Cryptography;

namespace Weir.Infrastructure.Runtime;

/// <summary>
/// Replaces a small file in Weir's data folder so a reader (the Windows tray watches several of these) sees the old
/// contents or the new ones, never half of either: the bytes go to a uniquely named scratch file first and are then
/// renamed into place.
/// </summary>
internal static class AtomicFileWriter
{
    internal static void Replace(string directory, string fileName, byte[] contents)
    {
        var path = Path.Join(directory, fileName);
        var scratch = Path.Join(directory, $".{fileName}.{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}.tmp");
        try
        {
            File.WriteAllBytes(scratch, contents);
            File.Move(scratch, path, overwrite: true);
            scratch = string.Empty;
        }
        finally
        {
            if (scratch.Length > 0 && File.Exists(scratch))
            {
                File.Delete(scratch);
            }
        }
    }
}
