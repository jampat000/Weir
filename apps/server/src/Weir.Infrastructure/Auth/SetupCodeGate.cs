using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Weir.Core.Configuration;
using Weir.Core.Net;

namespace Weir.Infrastructure.Auth;

/// <summary>
/// Guards <c>POST /api/v1/auth/bootstrap</c> once no admin account exists yet. Only a loopback caller, or
/// one that supplies the one-time setup code generated at start-up, can create the first account. The tray
/// opens Weir at <c>127.0.0.1</c>, so a Windows user on the same PC needs nothing extra; anyone reaching
/// bootstrap from elsewhere reads the code from Weir's own log or data folder.
/// </summary>
public sealed class SetupCodeGate
{
    /// <summary>Excludes 0/O and 1/I/L, which are easy to misread when copied from a log or a phone call.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int CodeGroupLength = 4;

    private readonly Lock _lock = new();
    private readonly string _path;
    private string? _currentCode;

    public SetupCodeGate(WeirOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _path = Path.Join(options.WeirHome, "setup-code");
    }

    /// <summary><see langword="true"/> while a non-loopback bootstrap attempt must supply a matching code.</summary>
    public bool HasCode
    {
        get
        {
            lock (_lock)
            {
                return _currentCode is not null;
            }
        }
    }

    /// <summary><paramref name="host"/> is a loopback address: the tray's own <c>127.0.0.1</c>, or the same machine's <c>::1</c>.</summary>
    public static bool IsLoopback(string? host) => host is not null && NetAddress.TryParse(host, out var address) && address.IsLoopback;

    /// <summary>
    /// Called once at startup: generates and publishes a new code while no admin exists yet, or clears any
    /// code (and stale file) left from an earlier run once one does.
    /// </summary>
    public void EnsureStateForStartup(bool adminExists, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        if (adminExists)
        {
            Clear();
            return;
        }

        var code = GenerateCode();
        lock (_lock)
        {
            _currentCode = code;
        }

        WriteOwnerOnlyFile(_path, code + Environment.NewLine);
        logger.LogWarning(
            "Weir has no account yet. To create one from another device, enter this setup code: {SetupCode}. It is also in {Path}.",
            code,
            _path);
    }

    /// <summary>Constant-time match against the code from the most recent <see cref="EnsureStateForStartup"/>.</summary>
    public bool Validate(string? suppliedCode)
    {
        string? expected;
        lock (_lock)
        {
            expected = _currentCode;
        }

        return expected is not null && suppliedCode is not null &&
            CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(suppliedCode.Trim().ToUpperInvariant()));
    }

    /// <summary>Called once an admin account is created: the code no longer opens anything.</summary>
    public void Clear()
    {
        lock (_lock)
        {
            _currentCode = null;
        }

        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
            // Best-effort: a stale file with no code behind it grants nothing.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private static string GenerateCode()
    {
        var first = RandomNumberGenerator.GetString(CodeAlphabet, CodeGroupLength);
        var second = RandomNumberGenerator.GetString(CodeAlphabet, CodeGroupLength);
        return $"{first}-{second}";
    }

    /// <summary>Writes <paramref name="contents"/> readable only by the account Weir runs as, where the platform allows it.</summary>
    private static void WriteOwnerOnlyFile(string path, string contents)
    {
        File.WriteAllText(path, contents, Encoding.UTF8);
        if (OperatingSystem.IsWindows())
        {
            var owner = System.Security.Principal.WindowsIdentity.GetCurrent().User!;
            var security = new System.Security.AccessControl.FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.SetOwner(owner);
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                owner, System.Security.AccessControl.FileSystemRights.FullControl, System.Security.AccessControl.AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
        else
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}
