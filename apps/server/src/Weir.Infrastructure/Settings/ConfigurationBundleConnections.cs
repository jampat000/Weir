using Weir.Core.Json;
using Weir.Core.MediaManagers;
using Weir.Core.Notifications;
using Weir.Infrastructure.MediaManagers;
using Weir.Infrastructure.Notifications;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Settings;

/// <summary>
/// The media managers and alerts in a configuration bundle (#693).
/// </summary>
/// <remarks>
/// A bundle is a plain file people keep and copy around, so it never carries a secret: no API key, no webhook
/// secret, and no alert address (a Discord or webhook address is itself the credential). A restore adds the ones
/// this install does not have yet, switched off and without those secrets, and leaves the ones it already has,
/// secrets included, exactly as they are.
/// </remarks>
public sealed class ConfigurationBundleConnections
{
    public const string MediaManagersSection = "media_manager_connections";
    public const string AlertsSection = "notification_channels";

    // Matches the create/update request models (MediaManagerEndpoints, NotificationEndpoints): a restored row
    // must fit the same columns a hand-typed one does.
    private const int MediaManagerNameMaxLength = 200;
    private const int AlertLabelMaxLength = 255;

    private readonly NotificationChannelStore _channels;

    public ConfigurationBundleConnections(NotificationChannelStore channels)
    {
        _channels = channels ?? throw new ArgumentNullException(nameof(channels));
    }

    public static async Task<WireArray> ExportMediaManagersAsync(UnitOfWork uow)
    {
        var connections = await MediaManagerConnectionStore.ListAsync(uow).ConfigureAwait(false);
        return new WireArray(connections.Select(connection => (WireValue)new WireObject()
            .Set("id", connection.Id)
            .Set("kind", connection.Kind)
            .Set("name", connection.Name)
            .Set("enabled", connection.Enabled)
            .Set("base_url", connection.BaseUrl)));
    }

    public async Task<WireArray> ExportAlertsAsync(UnitOfWork uow)
    {
        var channels = await _channels.ListAsync(uow).ConfigureAwait(false);
        return new WireArray(channels.Select(channel => (WireValue)new WireObject()
            .Set("label", channel.Label)
            .Set("provider", channel.Provider)
            .Set("events", new WireArray(NotificationRules.ParseEvents(channel.EventsJson).Select(e => (WireValue)new WireString(e))))
            .Set("enabled", channel.Enabled)));
    }

    /// <summary>
    /// Adds the bundle's media managers this install does not have (matched by name). Returns each exported
    /// connection id mapped to the id it has here, or <see langword="null"/> when the bundle has no media managers
    /// section, so the libraries that name a connection can be pointed at the right row.
    /// </summary>
    public static async Task<Dictionary<long, long>?> RestoreMediaManagersAsync(UnitOfWork uow, WireObject bundle)
    {
        if (bundle.Get(MediaManagersSection) is not WireArray rows)
        {
            return null;
        }

        var existing = (await MediaManagerConnectionStore.ListAsync(uow).ConfigureAwait(false))
            .ToDictionary(connection => connection.Name, connection => connection.Id, StringComparer.Ordinal);
        var restoredIds = new Dictionary<long, long>();
        foreach (var row in rows.Items)
        {
            var data = row as WireObject ?? throw new WireValueException("This backup's media managers are not in a form Weir can read.");
            var name = RequiredText(data, "name", MediaManagerNameMaxLength);
            var kind = RequiredText(data, "kind");
            if (!MediaManagerKinds.All.Contains(kind, StringComparer.Ordinal))
            {
                throw new WireValueException($"This backup has a media manager, {name}, of a kind this version of Weir does not support.");
            }

            var baseUrl = ValidateRestoredBaseUrl(name, OptionalText(data, "base_url"));
            if (!existing.TryGetValue(name, out var id))
            {
                id = await MediaManagerConnectionStore.InsertAsync(
                    uow, kind, name, enabled: false, baseUrl, apiKeyCiphertext: null).ConfigureAwait(false);
                existing[name] = id;
            }

            if (TryReadId(data.Get("id"), out var exportedId))
            {
                restoredIds[exportedId] = id;
            }
        }

        return restoredIds;
    }

    /// <summary>Adds the bundle's alerts this install does not have (matched by label and provider).</summary>
    public async Task RestoreAlertsAsync(UnitOfWork uow, WireObject bundle)
    {
        if (bundle.Get(AlertsSection) is not WireArray rows)
        {
            return;
        }

        var existing = (await _channels.ListAsync(uow).ConfigureAwait(false))
            .Select(channel => (channel.Label, channel.Provider))
            .ToHashSet();
        foreach (var row in rows.Items)
        {
            var data = row as WireObject ?? throw new WireValueException("This backup's alerts are not in a form Weir can read.");
            var label = RequiredText(data, "label", AlertLabelMaxLength).Trim();
            var provider = RequiredText(data, "provider");
            if (!NotificationRules.SupportedProviders.Contains(provider, StringComparer.Ordinal))
            {
                throw new WireValueException($"This backup has an alert, {label}, of a kind this version of Weir does not support.");
            }

            if (!existing.Add((label, provider)))
            {
                continue;
            }

            var events = data.Get("events") is WireArray list
                ? list.Items.OfType<WireString>().Select(item => item.Value).Where(e => NotificationRules.SupportedEvents.Contains(e, StringComparer.Ordinal)).ToList()
                : [];
            await _channels.CreateAsync(uow, label, provider, url: string.Empty, events, enabled: false).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Points a library row's <c>discovered_from_connection_id</c> at the connection's id on this install, or
    /// clears it when the bundle did not carry that connection.
    /// </summary>
    public static void RemapLibraryConnection(WireObject library, IReadOnlyDictionary<long, long> restoredIds)
    {
        const string column = "discovered_from_connection_id";
        var value = library.Get(column);
        if (value is not null and not WireNull)
        {
            library.Set(column, TryReadId(value, out var exportedId) && restoredIds.TryGetValue(exportedId, out var id) ? WireValue.Of(id) : WireNull.Instance);
        }
    }

    private static bool TryReadId(WireValue? value, out long id)
    {
        id = 0;
        if (value is not WireInteger number || number.Value < long.MinValue || number.Value > long.MaxValue)
        {
            return false;
        }

        id = (long)number.Value;
        return true;
    }

    private static string RequiredText(WireObject data, string key, int? maxLength = null)
    {
        if (data.Get(key) is not WireString text || text.Value.Trim().Length == 0)
        {
            throw new WireValueException("This backup is missing part of a media manager or alert. Download a fresh backup and try again.");
        }

        if (maxLength is { } limit && text.Value.Length > limit)
        {
            throw new WireValueException($"This backup's {key} is too long for Weir to store.");
        }

        return text.Value;
    }

    private static string OptionalText(WireObject data, string key) => data.Get(key) is WireString text ? text.Value : string.Empty;

    /// <summary>
    /// The same address policy a hand-typed connection is held to (<see cref="MediaManagerConnectionService.ValidateBaseUrl"/>),
    /// so a bad address cannot enter through a restore instead. Refuses the whole restore, naming the connection,
    /// rather than silently dropping or blanking the address.
    /// </summary>
    private static string ValidateRestoredBaseUrl(string connectionName, string rawBaseUrl)
    {
        try
        {
            return MediaManagerConnectionService.ValidateBaseUrl(rawBaseUrl);
        }
        catch (MediaManagerConnectionException exception)
        {
            throw new WireValueException($"This backup has a media manager, {connectionName}, with an address Weir will not use: {exception.Message}");
        }
    }
}
