using Microsoft.Extensions.Logging;
using Weir.Core.Json;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassHandler
{
    /// <summary>Tells the originating manager how the pass went. Best effort; never throws.</summary>
    private async Task ReportBackAsync(string? payloadJson, WireObject result)
    {
        if (_reporter is null)
        {
            return;
        }

        try
        {
            var uow = await UnitOfWork.OpenAsync(_database).ConfigureAwait(false);
            await using (uow.ConfigureAwait(false))
            {
                var status = await _reporter.ReportHandoffCompletionAsync(uow, payloadJson, result).ConfigureAwait(false);
                if (!status.StartsWith("skipped", StringComparison.Ordinal))
                {
                    _logger.LogInformation("Hand-off callback: {Status}", status);
                }
            }
        }
#pragma warning disable CA1031 // Reporting must never break the job.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            _logger.LogError(exception, "Hand-off callback raised unexpectedly.");
        }
    }
}
