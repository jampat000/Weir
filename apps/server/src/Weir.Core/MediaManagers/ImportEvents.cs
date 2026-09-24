using System.Numerics;
using Weir.Core.Json;

namespace Weir.Core.MediaManagers;

/// <summary>
/// A file a media manager wants Weir to act on.
/// <c>imported</c> is a manager saying it imported a file (#652): when that file is one Weir handed back, Weir records it
/// and may release its copy. <c>handoff</c> is Processing's cue and the only kind with a callback.
/// </summary>
public sealed record MediaManagerImportEvent
{
    public const string Imported = "imported";
    public const string Handoff = "handoff";

    public required string SourceKey { get; init; }
    public required string EventKind { get; init; }
    public required string MediaScope { get; init; }
    public required string FilePath { get; init; }
    public string? Title { get; init; }
    public BigInteger? Year { get; init; }
    public string? ShowTitle { get; init; }
    public BigInteger? SeasonNumber { get; init; }
    public BigInteger? EpisodeNumber { get; init; }
    public string? EpisodeTitle { get; init; }
    public BigInteger? SourceEntityId { get; init; }
    public string? HandoffId { get; init; }
    public string? CallbackPath { get; init; }
    public string? ReleaseName { get; init; }

    /// <summary>The manager's own library id; Deluno refuses a processor event without it.</summary>
    public string? LibraryId { get; init; }

    /// <summary>
    /// For an <c>imported</c> event, the file the manager imported from, as the manager sees it (Sonarr's
    /// <c>episodeFile.sourcePath</c>, Radarr's <c>movieFile.sourcePath</c>). When Weir handed the file back, this is Weir's
    /// copy in the output folder. <see cref="FilePath"/> is where the file ended up in the manager's library.
    /// </summary>
    public string? SourcePath { get; init; }

    /// <summary>The download client's id for the download, when the manager sent one (Sonarr's and Radarr's <c>downloadId</c>).</summary>
    public string? DownloadId { get; init; }
}

/// <summary>How one manager phrases an inbound event.</summary>
public sealed record MediaManagerDialect(string Key, string DisplayName, Func<WireObject, MediaManagerImportEvent?> Normalize);

/// <summary>The inbound dialects.</summary>
public static class ImportEvents
{
    /// <summary>Every dialect by source key.</summary>
    public static IReadOnlyDictionary<string, MediaManagerDialect> Dialects { get; } = new[]
    {
        new MediaManagerDialect("radarr", "Radarr", NormalizeRadarr),
        new MediaManagerDialect("sonarr", "Sonarr", NormalizeSonarr),
        new MediaManagerDialect("deluno", "Deluno", NormalizeDeluno),
        new MediaManagerDialect("native", "Generic (Weir native payload)", NormalizeNative),
    }.ToDictionary(d => d.Key, StringComparer.Ordinal);

    /// <summary>The dialect for a source key, case- and whitespace-insensitive.</summary>
    public static MediaManagerDialect? DialectForSource(string? sourceKey) =>
        Dialects.GetValueOrDefault(WireStrings.Strip(sourceKey ?? string.Empty).ToLowerInvariant());

    /// <summary>Every source key, sorted.</summary>
    public static IReadOnlyList<string> KnownSourceKeys() => [.. Dialects.Keys.Order(StringComparer.Ordinal)];

    private static WireObject? Mapping(WireObject body, string key) => body.Get(key) as WireObject;

    private static MediaManagerImportEvent? NormalizeSonarr(WireObject body)
    {
        if (ManagerValues.Text(body.Get("eventType")) != "Download")
        {
            return null;
        }

        if (body.Get("episodes") is not WireArray { Items.Count: > 0 } episodes || episodes.Items[0] is not WireObject episode)
        {
            return null;
        }

        var episodeFile = Mapping(body, "episodeFile");
        var path = episodeFile is null ? null : ManagerValues.Text(episodeFile.Get("path"));
        if (path is null)
        {
            return null;
        }

        var series = Mapping(body, "series");
        var seriesTitle = series is null ? null : ManagerValues.Text(series.Get("title"));
        return new MediaManagerImportEvent
        {
            SourceKey = "sonarr",
            EventKind = MediaManagerImportEvent.Imported,
            MediaScope = "tv",
            FilePath = path,
            Title = seriesTitle,
            ShowTitle = seriesTitle,
            SeasonNumber = ManagerValues.WholeNumber(episode.Get("seasonNumber")),
            EpisodeNumber = ManagerValues.WholeNumber(episode.Get("episodeNumber")),
            EpisodeTitle = ManagerValues.Text(episode.Get("title")),
            SourceEntityId = ManagerValues.WholeNumber(episode.Get("id")),
            SourcePath = ManagerValues.Text(episodeFile!.Get("sourcePath")),
            DownloadId = ManagerValues.Text(body.Get("downloadId")),
        };
    }

    private static MediaManagerImportEvent? NormalizeRadarr(WireObject body)
    {
        if (ManagerValues.Text(body.Get("eventType")) != "Download")
        {
            return null;
        }

        var movie = Mapping(body, "movie");
        if (movie is null)
        {
            return null;
        }

        var movieFile = Mapping(body, "movieFile");
        var path = movieFile is null ? null : ManagerValues.Text(movieFile.Get("path"));
        if (path is null)
        {
            return null;
        }

        return new MediaManagerImportEvent
        {
            SourceKey = "radarr",
            EventKind = MediaManagerImportEvent.Imported,
            MediaScope = "movie",
            FilePath = path,
            Title = ManagerValues.Text(movie.Get("title")),
            Year = ManagerValues.WholeNumber(movie.Get("year")),
            SourceEntityId = ManagerValues.WholeNumber(movie.Get("id")),
            SourcePath = ManagerValues.Text(movieFile!.Get("sourcePath")),
            DownloadId = ManagerValues.Text(body.Get("downloadId")),
        };
    }

    /// <summary>The scope an inbound media type names (<c>movie</c> or <c>tv</c>), or null for a spelling it does not accept.</summary>
    public static string? ScopeFromMediaType(WireValue? raw)
    {
        var value = (ManagerValues.Text(raw) ?? string.Empty).ToLowerInvariant();
        return value switch
        {
            "movie" or "movies" or "film" => "movie",
            "tv" or "series" or "show" or "episode" => "tv",
            _ => null,
        };
    }

    private static MediaManagerImportEvent? NormalizeDeluno(WireObject body)
    {
        if (ManagerValues.Text(body.Get("eventType")) != "deluno.processor-handoff")
        {
            return null;
        }

        var path = ManagerValues.Text(body.Get("sourcePath"));
        var scope = ScopeFromMediaType(body.Get("mediaType"));
        if (path is null || scope is null)
        {
            return null;
        }

        var releaseName = ManagerValues.Text(body.Get("releaseName"));
        return new MediaManagerImportEvent
        {
            SourceKey = "deluno",
            EventKind = MediaManagerImportEvent.Handoff,
            MediaScope = scope,
            FilePath = path,
            Title = releaseName,
            ReleaseName = releaseName,
            HandoffId = ManagerValues.Text(body.Get("handoffId")),
            CallbackPath = ManagerValues.Text(body.Get("callbackPath")),
            LibraryId = ManagerValues.Text(body.Get("libraryId")),
            DownloadId = ManagerValues.Text(body.Get("downloadId")),
        };
    }

    private static MediaManagerImportEvent? NormalizeNative(WireObject body)
    {
        var kind = (ManagerValues.Text(body.Get("event")) ?? MediaManagerImportEvent.Imported).ToLowerInvariant();
        if (kind is not (MediaManagerImportEvent.Imported or MediaManagerImportEvent.Handoff))
        {
            return null;
        }

        var path = ManagerValues.Text(body.Get("filePath")) ?? ManagerValues.Text(body.Get("file_path"));
        var scope = ScopeFromMediaType(ManagerValues.Or(body.Get("mediaScope"), body.Get("media_scope")));
        if (path is null || scope is null)
        {
            return null;
        }

        WireValue? Either(string camel, string snake) => ManagerValues.Or(body.Get(camel), body.Get(snake));
        return new MediaManagerImportEvent
        {
            SourceKey = "native",
            EventKind = kind,
            MediaScope = scope,
            FilePath = path,
            Title = ManagerValues.Text(body.Get("title")),
            Year = ManagerValues.WholeNumber(body.Get("year")),
            ShowTitle = ManagerValues.Text(Either("showTitle", "show_title")),
            SeasonNumber = ManagerValues.WholeNumber(Either("seasonNumber", "season_number")),
            EpisodeNumber = ManagerValues.WholeNumber(Either("episodeNumber", "episode_number")),
            EpisodeTitle = ManagerValues.Text(Either("episodeTitle", "episode_title")),
            SourceEntityId = ManagerValues.WholeNumber(Either("entityId", "entity_id")),
            HandoffId = ManagerValues.Text(Either("handoffId", "handoff_id")),
            CallbackPath = ManagerValues.Text(Either("callbackPath", "callback_path")),
            ReleaseName = ManagerValues.Text(Either("releaseName", "release_name")),
            LibraryId = ManagerValues.Text(Either("libraryId", "library_id")),
            SourcePath = ManagerValues.Text(Either("sourcePath", "source_path")),
            DownloadId = ManagerValues.Text(Either("downloadId", "download_id")),
        };
    }
}
