using Microsoft.Extensions.Logging;
using Weir.Core.Media;
using Weir.Infrastructure.Media;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// The safe swap's <see cref="ISwapOutputValidator"/> (#506): the same full-read integrity check the download pipeline
/// has (<see cref="MediaTools.ValidateMediaIntegrityAsync"/> — demuxes the whole cleaned copy and rejects a truncated or
/// otherwise incomplete file), cross-checked against the original's probed duration when that can be read. The #500
/// track-by-track check against the plan has already run by then: the clean writes its copy through
/// <see cref="MediaTools.RemuxToTempFileAsync"/>, which validates the staged output before returning it.
/// </summary>
public sealed class RemuxOutputSwapValidator : ISwapOutputValidator
{
    private readonly MediaTools _tools;
    private readonly ILogger<RemuxOutputSwapValidator> _logger;

    public RemuxOutputSwapValidator(MediaTools tools, ILogger<RemuxOutputSwapValidator> logger)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SwapValidation> ValidateAsync(string originalPath, string outputPath, CancellationToken cancellationToken)
    {
        double? expectedDuration = null;
        try
        {
            var probe = await _tools.FfprobeJsonAsync(originalPath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (probe.TryGetProperty("format", out var format) && format.TryGetProperty("duration", out var durationValue) &&
                durationValue.ValueKind == System.Text.Json.JsonValueKind.String &&
                double.TryParse(durationValue.GetString(), System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                expectedDuration = parsed;
            }
        }
        catch (Exception exception) when (exception is MediaToolException or MediaToolTimeoutException)
        {
            // The original's duration is only a cross-check; the integrity read below still runs without it.
            _logger.LogDebug(exception, "Library swap validator could not re-read the original's duration; checking without it.");
        }

        try
        {
            await _tools.ValidateMediaIntegrityAsync(outputPath, expectedDuration, cancellationToken).ConfigureAwait(false);
            return SwapValidation.Pass;
        }
        catch (Exception exception) when (exception is MediaToolException or MediaToolTimeoutException)
        {
            return SwapValidation.Fail(exception.Message);
        }
    }
}
