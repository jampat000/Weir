using Microsoft.Data.Sqlite;

namespace Weir.Contract.Tests.Libraries;

/// <summary>A server whose first library holds the small, deliberately varied scan index of <see cref="LibraryViewSeed.SeedScanIndex"/>.</summary>
public class ScannedLibraryFixture : SeededServerFixture
{
    public long LibraryId { get; private set; }

    protected override void Seed(SqliteConnection connection) => LibraryId = LibraryViewSeed.SeedScanIndex(connection);
}
