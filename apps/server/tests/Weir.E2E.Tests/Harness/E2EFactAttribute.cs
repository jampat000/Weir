using Xunit.Abstractions;
using Xunit.Sdk;

namespace Weir.E2E.Tests.Harness;

/// <summary>
/// A browser test. It is in <c>Category=E2E</c>, so <c>--filter "Category!=E2E"</c> leaves it out, and it is skipped
/// unless <c>WEIR_E2E=1</c>, so a plain <c>dotnet test</c> never starts a browser by accident.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
[TraitDiscoverer(E2ECategoryDiscoverer.TypeName, E2ECategoryDiscoverer.AssemblyName)]
public sealed class E2EFactAttribute : FactAttribute, ITraitAttribute
{
    public const string EnableVariable = "WEIR_E2E";

    public E2EFactAttribute()
    {
        if (!IsEnabled)
        {
            Skip = $"Weir E2E requires {EnableVariable}=1 (see apps/server/tests/Weir.E2E.Tests/README.md).";
        }
    }

    public static bool IsEnabled => Environment.GetEnvironmentVariable(EnableVariable) == "1";
}

public sealed class E2ECategoryDiscoverer : ITraitDiscoverer
{
    public const string TypeName = "Weir.E2E.Tests.Harness.E2ECategoryDiscoverer";
    public const string AssemblyName = "Weir.E2E.Tests";

    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>("Category", "E2E");
    }
}
