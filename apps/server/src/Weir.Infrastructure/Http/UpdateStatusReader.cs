using Weir.Core;
using Weir.Core.Configuration;
using Weir.Core.Json;
using Weir.Core.Updates;
using Weir.Infrastructure.Runtime;

namespace Weir.Infrastructure.Http;

/// <summary>
/// The update status of this install: what the latest published release is and whether this one is behind it. A check that
/// fails for any reason reads as unavailable, never as an error.
/// </summary>
public sealed class UpdateStatusReader
{
    /// <summary>The version reported when the build carries none.</summary>
    private const string UnknownVersion = "0.0.0";

    private readonly IReleaseCatalogClient _releases;
    private readonly WeirOptions _options;

    public UpdateStatusReader(IReleaseCatalogClient releases, WeirOptions options)
    {
        _releases = releases ?? throw new ArgumentNullException(nameof(releases));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>The <c>GET /suite/update-status</c> body.</summary>
    public async Task<WireObject> ReadAsync(CancellationToken cancellationToken)
    {
        var installType = UpdateFiles.DetectInstallType(_options.RuntimeKind);
        var currentVersion = WeirVersion.Resolve(_options.VersionOverride);
        if (currentVersion.Length == 0)
        {
            currentVersion = UnknownVersion;
        }

        try
        {
            var release = await _releases.FetchLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
            return UpdateStatus.FromRelease(currentVersion, installType, release);
        }
        catch (ReleaseFetchException exception) when (exception.StatusCode == 404)
        {
            return UpdateStatus.Unavailable(currentVersion, installType, "not_published", "No public Weir release is published yet.");
        }
#pragma warning disable CA1031 // An update check that fails for any reason reads as unavailable, never an error page.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
        {
            return UpdateStatus.Unavailable(currentVersion, installType, "unavailable", "Could not check for updates right now.");
        }
    }
}
