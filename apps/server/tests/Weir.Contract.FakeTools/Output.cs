using System.Text;

namespace Weir.Contract.FakeTools;

/// <summary>Standard output and error as UTF-8 without a byte-order mark, on every platform.</summary>
internal static class Output
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Out(string text) => Console.OpenStandardOutput().Write(Utf8.GetBytes(text));

    public static void Error(string line) => Console.OpenStandardError().Write(Utf8.GetBytes(line + "\n"));
}
