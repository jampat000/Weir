using Weir.Core.Configuration;
using Weir.Core.Processing;
using Weir.Infrastructure.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.SystemReadings;

/// <summary>What the sampler needs to know about the work Weir is set up to do: the folders its workflows use and how many files it runs at once.</summary>
public sealed record WorkSetup(IReadOnlyList<WorkflowFolder> Folders, int Slots);

/// <summary>Where the sampler learns <see cref="WorkSetup"/>; behind an interface so tests need no database.</summary>
public interface IWorkSource
{
    Task<WorkSetup> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>The saved workflows and the saved files-at-once setting, which the worker count Weir was started with caps.</summary>
public sealed class DatabaseWorkSource(
    SqliteDatabase database,
    LibraryStore workflows,
    OperatorSettingsStore operatorSettings,
    WeirOptions options) : IWorkSource
{
    public async Task<WorkSetup> ReadAsync(CancellationToken cancellationToken)
    {
        var uow = await UnitOfWork.OpenAsync(database, cancellationToken).ConfigureAwait(false);
        await using (uow.ConfigureAwait(false))
        {
            var saved = await workflows.ListAsync(uow).ConfigureAwait(false);
            var settings = await operatorSettings.GetAsync(uow).ConfigureAwait(false);
            return new WorkSetup(WorkflowFolders.Of(saved, options.WeirHome), Slots(settings));
        }
    }

    /// <summary>The same limit the files-at-once readout reports: the saved setting, or the worker slots if there are fewer.</summary>
    private int Slots(ProcessingOperatorSettingsRecord? settings)
    {
        var wanted = settings is null ? options.ProcessingWorkerCount : OperatorSettingsRules.ClampMaxConcurrentFiles(settings.MaxConcurrentFiles);
        return (int)Math.Max(0, Math.Min(wanted, options.ProcessingWorkerCount));
    }
}
