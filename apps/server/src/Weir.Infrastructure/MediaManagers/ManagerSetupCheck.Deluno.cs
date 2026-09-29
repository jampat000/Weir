using System.Net;
using Microsoft.Extensions.Logging;
using Weir.Core.MediaManagers;

namespace Weir.Infrastructure.MediaManagers;

public sealed partial class ManagerSetupCheck
{
    /// <summary>Deluno's manifest, or the setup result that says why there is none.</summary>
    private sealed record DelunoManifest(ManagerDescription? Description, DelunoSetupResult? Failure);

    private async Task<DelunoManifest> ReadDelunoManifestAsync(ManagerConnection connection, string label, CancellationToken cancellationToken)
    {
        if (_connections.Ports.PortForKind(connection.Kind) is not { } port)
        {
            return new DelunoManifest(null, new DelunoSetupResult(null, null, [new SetupCheckLine(SetupCheckLine.Problem, $"Weir does not know how to ask {label} what it manages.")]));
        }

        var description = await port.DescribeAsync(connection, cancellationToken).ConfigureAwait(false);
        return description.Status == SignalStatus.Reported
            ? new DelunoManifest(description, null)
            : new DelunoManifest(null, new DelunoSetupResult(null, null, [new SetupCheckLine(SetupCheckLine.Problem, description.Detail ?? $"{label} did not answer.")]));
    }

    /// <summary>
    /// Deluno's manifest and, for the library the workflow is checked against, where its downloads land and how its path
    /// mappings apply. That is one manifest read and one download-destinations read per connection.
    /// </summary>
    private async Task<(DelunoSetupResult Result, ManagerSourceFacts Facts)> CheckDelunoAsync(
        ManagerConnection connection,
        string label,
        string mediaScope,
        string watchedFolder,
        string outputFolder,
        string? preferredLibraryKey,
        CancellationToken cancellationToken)
    {
        var manifest = await ReadDelunoManifestAsync(connection, label, cancellationToken).ConfigureAwait(false);
        if (manifest.Description is not { } description)
        {
            return (manifest.Failure!, ManagerSourceFacts.None);
        }

        var facts = ManagerSourceFactsRules.ForDeluno(mediaScope, description.Libraries, description.DownloadClients);
        var choice = ManagerSetupRules.ChooseDelunoLibrary(label, mediaScope, description.Libraries, preferredLibraryKey);
        if (choice.Library is not { } library)
        {
            return (new DelunoSetupResult(null, null, choice.Lines), facts);
        }

        var answer = await ReadDestinationsAsync(connection, library.Key, cancellationToken).ConfigureAwait(false);
        return (ManagerSetupRules.EvaluateDelunoWithDestinations(label, mediaScope, watchedFolder, outputFolder, choice, description.DownloadClients, answer, _folders), facts);
    }

    /// <summary>The folders Deluno's manifest reports for the media type, with no workflow to compare them with and no second read.</summary>
    private async Task<DelunoSetupResult> ReadDelunoFoldersAsync(ManagerConnection connection, string mediaScope, CancellationToken cancellationToken)
    {
        var manifest = await ReadDelunoManifestAsync(connection, connection.Label, cancellationToken).ConfigureAwait(false);
        return manifest.Description is { } description
            ? ManagerSetupRules.EvaluateDeluno(connection.Label, mediaScope, string.Empty, string.Empty, description.Libraries, description.DownloadClients, _folders)
            : manifest.Failure!;
    }

    /// <summary>
    /// <c>GET /api/integrations/processors/download-destinations?libraryId=</c>. A 404 is a Deluno that predates the route
    /// (the library id comes from the manifest just read), and a 403 is a key without the Imports scope.
    /// </summary>
    private async Task<DelunoDestinationsAnswer> ReadDestinationsAsync(ManagerConnection connection, string libraryKey, CancellationToken cancellationToken)
    {
        try
        {
            var client = new MediaManagerHttpClient(connection.BaseUrl, connection.ApiKey, _handlers, ManagerDialectRules.DownloadDestinationsTimeout);
            var payload = await client
                .GetJsonAsync(DelunoDestinationRules.DownloadDestinationsPath, [new("libraryId", libraryKey)], cancellationToken)
                .ConfigureAwait(false);
            return DelunoDestinationsAnswer.Read(DelunoDestinationRules.ParseLibraries(payload));
        }
        catch (MediaManagerHttpException exception) when (exception.StatusCode == (int)HttpStatusCode.NotFound)
        {
            LogDestinationsNotOffered(_logger, connection.Label);
            return DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NotOffered);
        }
        catch (MediaManagerHttpException exception) when (exception.StatusCode == (int)HttpStatusCode.Forbidden)
        {
            LogDestinationsNeedScope(_logger, connection.Label);
            return DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.NeedsImportsScope);
        }
        catch (Exception exception) when (exception is MediaManagerHttpException or MediaManagerUnreachableException)
        {
            return DelunoDestinationsAnswer.Failed(DelunoDestinationsStatus.Unreachable, UnreachableText(connection, exception, "where its downloads are saved"));
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Label} has no download-destinations route, so it is older than the release that added it.")]
    private static partial void LogDestinationsNotOffered(ILogger logger, string label);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Label} refused the download-destinations read: its API key lacks the Imports scope.")]
    private static partial void LogDestinationsNeedScope(ILogger logger, string label);
}
