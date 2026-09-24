using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Weir.Core.Activity;
using Weir.Core.Json;
using Weir.Core.Media;
using Weir.Core.MediaManagers;
using Weir.Infrastructure.Activity;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.MediaManagers;

/// <summary>
/// The optional downloaded-scan hand-back (#768): for every enabled Sonarr/Radarr connection linked to a library
/// that has opted in, ask it to run its Downloaded Scan command over a file a live pass just wrote to that
/// library's output folder. Read only towards the manager's remote path mappings, one write towards it (the
/// command); never throws, since a manager that cannot be reached must not fail a pass that already succeeded.
/// </summary>
public sealed class DownloadedScanNotifier
{
    private readonly MediaManagerConnectionService _connections;
    private readonly IManagerHttpHandlerFactory _handlers;
    private readonly ILogger _logger;

    public DownloadedScanNotifier(MediaManagerConnectionService connections, IManagerHttpHandlerFactory handlers, ILogger<DownloadedScanNotifier>? logger = null)
    {
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _handlers = handlers ?? throw new ArgumentNullException(nameof(handlers));
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Notifies every qualifying connection linked to <paramref name="libraryId"/>: enabled, Sonarr or Radarr,
    /// opted in (<c>downloaded_scan_enabled</c>), and covering <paramref name="mediaScope"/>. Commits
    /// <paramref name="uow"/> when it recorded anything.
    /// </summary>
    public async Task NotifyLibraryAsync(
        UnitOfWork uow, long libraryId, string mediaScope, string outputPath, string relativeMediaPath, int? downloadClientId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(uow);
        var wroteAnything = false;
        foreach (var connectionId in await LibraryStore.ManagerConnectionIdsAsync(uow, libraryId).ConfigureAwait(false))
        {
            var row = await MediaManagerConnectionStore.GetAsync(uow, connectionId).ConfigureAwait(false);
            if (row is null || !row.Enabled || !row.DownloadedScanEnabled)
            {
                continue;
            }

            if (ManagerKindProfiles.ForKind(row.Kind) is not { IsArr: true } profile || profile.ArrScope != mediaScope)
            {
                continue;
            }

            if (_connections.ConnectionFromRow(row) is not { } connection)
            {
                continue;
            }

            await NotifyOneAsync(uow, connection, profile.ArrScope, outputPath, relativeMediaPath, downloadClientId, cancellationToken).ConfigureAwait(false);
            wroteAnything = true;
        }

        if (wroteAnything)
        {
            await uow.CommitAsync().ConfigureAwait(false);
        }
    }

    private async Task NotifyOneAsync(
        UnitOfWork uow, ManagerConnection connection, string arrScope, string outputPath, string relativeMediaPath, int? downloadClientId,
        CancellationToken cancellationToken)
    {
        var mappings = await MappingsAsync(connection, cancellationToken).ConfigureAwait(false);
        var managerPath = DownloadedScanRules.TranslateOutputPath(outputPath, mappings);
        var body = DownloadedScanRules.CommandBody(arrScope, managerPath, downloadClientId);
        try
        {
            var client = new MediaManagerHttpClient(connection.BaseUrl, connection.ApiKey, _handlers, ManagerDialectRules.DescribeTimeout);
            await client.PostJsonAsync(DownloadedScanRules.CommandPath, body, cancellationToken: cancellationToken).ConfigureAwait(false);
            await RecordAsync(uow, connection, relativeMediaPath, body, accepted: true, failureReason: null).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or MediaManagerUnreachableException)
        {
            _logger.LogWarning("Downloaded scan request to {Manager} failed: {Error}", connection.Label, exception.Message);
            await RecordAsync(uow, connection, relativeMediaPath, body, accepted: false, failureReason: exception.Message).ConfigureAwait(false);
        }
    }

    /// <summary>The manager's remote path mappings, or none when they cannot be read; the command is still sent either way.</summary>
    private async Task<List<RemotePathMappingEntry>> MappingsAsync(ManagerConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var client = new MediaManagerHttpClient(connection.BaseUrl, connection.ApiKey, _handlers, ManagerDialectRules.DescribeTimeout);
            var raw = await client.GetJsonAsync(ManagerSetupRules.RemotePathMappingPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            return ManagerSetupRules.ParseMappings(raw);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or MediaManagerUnreachableException)
        {
            return [];
        }
    }

    private static Task<long> RecordAsync(UnitOfWork uow, ManagerConnection connection, string relativeMediaPath, PyDict body, bool accepted, string? failureReason)
    {
        var fileName = relativeMediaPath.Length > 0 ? MediaPathNames.Name(relativeMediaPath, OperatingSystem.IsWindows()) : "a file Weir just wrote";
        var title = accepted
            ? $"Asked {connection.Label} to scan for {fileName}"
            : $"Weir could not ask {connection.Label} to scan for {fileName}";
        var detail = new PyDict()
            .Set("relative_media_path", relativeMediaPath)
            .Set("manager", connection.Label)
            .Set("command", body)
            .Set("accepted", accepted)
            .Set("trigger", "worker")
            .Set("result", accepted ? "success" : "failed");
        if (failureReason is not null)
        {
            detail.Set("reason", failureReason);
        }

        return SqliteActivityWriter.RecordAsync(uow, new ActivityEventDraft(
            ActivityEventTypes.ProcessingDownloadedScanRequested,
            "processing",
            title,
            PyStrings.Slice(PyJsonWriter.Dumps(detail, PyJsonFormat.Compact), 10_000)));
    }
}
