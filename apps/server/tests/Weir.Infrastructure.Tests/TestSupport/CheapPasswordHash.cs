using System.Security.Cryptography;
using System.Text;
using Weir.Core.Security;

namespace Weir.Infrastructure.Tests;

/// <summary>
/// A well-formed Argon2id hash at the smallest cost the verifier accepts, for a test that needs an account to
/// exist and sign in. Verification reads its cost from the stored hash, so a hash like this signs in through the
/// same code as a production one, in a millisecond instead of a second. The production cost is covered by the
/// tests that create the hash through the product (bootstrap, password change, recovery) and by the compatibility
/// tests in Weir.Core.Tests.
/// </summary>
internal static class CheapPasswordHash
{
    private const int TimeCost = 1;
    private const int MemoryKib = 8;
    private const int Parallelism = 1;
    private const int SaltLength = 16;
    private const int TagLength = 32;

    public static string For(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltLength);
        var tag = Argon2.Hash(Argon2Type.Argon2id, Argon2.Version13, Encoding.UTF8.GetBytes(password), salt, [], [], TimeCost, MemoryKib, Parallelism, TagLength);
        return $"$argon2id$v={Argon2.Version13}$m={MemoryKib},t={TimeCost},p={Parallelism}${Unpadded(salt)}${Unpadded(tag)}";
    }

    private static string Unpadded(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');
}
