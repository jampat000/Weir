using Weir.Core.Json;

namespace Weir.Infrastructure.Processing.RemuxPass;

public sealed partial class RemuxPassRunner
{
    /// <summary>
    /// Says which step the pass is on, for the moments between the write's own progress lines. <paramref name="status"/> is
    /// <c>processing</c> until the write is done and <c>finishing</c> after it, which is what puts a file under Handing
    /// back on screen.
    /// </summary>
    private static void ReportStage(Action<WireObject>? report, string relativeMediaPath, string status, string stage, string message) =>
        report?.Invoke(new WireObject()
            .Set("status", status)
            .Set("stage", stage)
            .Set("relative_media_path", relativeMediaPath)
            .Set("message", message));
}
