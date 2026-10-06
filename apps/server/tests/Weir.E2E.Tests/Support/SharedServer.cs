using Weir.E2E.Tests.Harness;

namespace Weir.E2E.Tests.Support;

/// <summary>Every browser test class is in this collection: one server, run one test at a time.</summary>
[CollectionDefinition(Name)]
public sealed class SharedServer : ICollectionFixture<E2EServer>
{
    public const string Name = "Weir E2E";
}
