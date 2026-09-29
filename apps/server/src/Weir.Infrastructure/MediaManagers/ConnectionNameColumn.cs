using Microsoft.Data.Sqlite;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// Keeps the <c>name</c> column of a connections table equal to what <see cref="ConnectionNaming"/> derives from
/// each row's kind and address (#826). The column stays so a backup still restores, and it is unique, so a row
/// is created under a placeholder and renamed with the rest.
/// </summary>
internal static class ConnectionNameColumn
{
    private const string PlaceholderPrefix = "renaming-";

    /// <summary>A name no real connection has, for a row about to be renamed.</summary>
    public static string NewPlaceholder() => PlaceholderPrefix + Guid.NewGuid().ToString("N");

    /// <summary>
    /// Renames every row of <paramref name="table"/> whose stored name is not its derived one. Two passes, because
    /// two rows can swap names and the column is unique. <paramref name="table"/> is a constant of the calling store.
    /// </summary>
    public static async Task RefreshAsync(UnitOfWork uow, string table, Func<string, string> productForKind)
    {
        ArgumentNullException.ThrowIfNull(uow);
        ArgumentNullException.ThrowIfNull(productForKind);
        var rows = await uow.QueryAsync($"SELECT id, kind, base_url, name FROM {table} ORDER BY id", ReadRow).ConfigureAwait(false);
        var derived = ConnectionNaming.NamesFor([.. rows.Select(row => new ConnectionAddress(row.Id, productForKind(row.Kind), row.BaseUrl))]);
        var renames = rows.Where(row => derived[row.Id] != row.Name).ToList();
        foreach (var row in renames)
        {
            await SetNameAsync(uow, table, row.Id, NewPlaceholder()).ConfigureAwait(false);
        }

        foreach (var row in renames)
        {
            await SetNameAsync(uow, table, row.Id, derived[row.Id]).ConfigureAwait(false);
        }
    }

    private static Task<int> SetNameAsync(UnitOfWork uow, string table, long id, string name) =>
        uow.ExecuteAsync($"UPDATE {table} SET name = $name WHERE id = $id", ("$name", name), ("$id", id));

    private static (long Id, string Kind, string BaseUrl, string Name) ReadRow(SqliteDataReader reader) => (
        SqliteValues.GetInt64(reader, 0),
        SqliteValues.GetString(reader, 1),
        SqliteValues.GetString(reader, 2),
        SqliteValues.GetString(reader, 3));
}
