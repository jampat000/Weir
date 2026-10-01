using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Artwork;

/// <summary>The Artwork setting on the Rules page: on unless a person switched it off.</summary>
public static class ArtworkSwitch
{
    /// <summary>Whether lookups run and posters show. A database that has no settings row yet is on, the setting's default.</summary>
    public static async Task<bool> IsOnAsync(UnitOfWork uow)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var value = await uow.ScalarAsync("SELECT artwork_enabled FROM suite_settings WHERE id = 1").ConfigureAwait(false);
        return value is null or DBNull || Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != 0;
    }
}
