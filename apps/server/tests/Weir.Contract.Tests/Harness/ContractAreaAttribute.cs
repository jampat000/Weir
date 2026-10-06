using Xunit.Abstractions;
using Xunit.Sdk;

namespace Weir.Contract.Tests.Harness;

/// <summary>
/// Puts a test class in one contract area (one folder of this project):
/// <c>dotnet test --filter Area=activity</c> runs that area alone. Every contract class is also in
/// <c>Category=Contract</c>, so the server test projects' default runs can leave the black-box suite out.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
[TraitDiscoverer(ContractAreaDiscoverer.TypeName, ContractAreaDiscoverer.AssemblyName)]
public sealed class ContractAreaAttribute(string area) : Attribute, ITraitAttribute
{
    public string Area { get; } = area;
}

public sealed class ContractAreaDiscoverer : ITraitDiscoverer
{
    public const string TypeName = "Weir.Contract.Tests.Harness.ContractAreaDiscoverer";
    public const string AssemblyName = "Weir.Contract.Tests";

    public const string AreaTrait = "Area";
    public const string CategoryTrait = "Category";
    public const string ContractCategory = "Contract";

    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        var area = traitAttribute.GetConstructorArguments().Single()?.ToString()
            ?? throw new InvalidOperationException("A contract area attribute needs the area's name.");
        yield return new KeyValuePair<string, string>(AreaTrait, area);
        yield return new KeyValuePair<string, string>(CategoryTrait, ContractCategory);
    }
}
