using Weir.Core.Media;
using Weir.Infrastructure.Media;

namespace Weir.Infrastructure.LibraryMode;

/// <summary>
/// The safe swap's <see cref="ISwapOutputValidator"/> (#506): the same full-read integrity check the download pipeline
/// has (<see cref="MediaTools.ValidateMediaIntegrityAsync"/> — demuxes the whole cleaned copy and rejects a truncated or
/// otherwise incomplete file), cross-checked against the original's duration when the clean's own probe found one. The #500
/// track-by-track check against the plan has already run by then: the clean writes its copy through
/// <see cref="MediaTools.RemuxToTempFileAsync"/>, which validates the staged output before returning it.
/// </summary>
public sealed class RemuxOutputSwapValidator : ISwapOutputValidator
{
    private readonly MediaTools _tools;

    public RemuxOutputSwapValidator(MediaTools tools)
    {
        _tools = tools ?? throw new ArgumentNullException(nameof(tools));
    }

    public async Task<SwapValidation> ValidateAsync(string originalPath, string outputPath, double? originalDurationSeconds, CancellationToken cancellationToken)
    {
        try
        {
            await _tools.ValidateMediaIntegrityAsync(outputPath, originalDurationSeconds, cancellationToken).ConfigureAwait(false);
            return SwapValidation.Pass;
        }
        catch (Exception exception) when (exception is MediaToolException or MediaToolTimeoutException)
        {
            return SwapValidation.Fail(exception.Message);
        }
    }
}
