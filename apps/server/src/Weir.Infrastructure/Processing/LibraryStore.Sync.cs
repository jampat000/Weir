using Weir.Core.Processing;
using Weir.Infrastructure.Sqlite;

namespace Weir.Infrastructure.Processing;

/// <summary>The writes a media manager that reports its folders makes to a workflow (<c>ManagerWorkflowSync</c>).</summary>
public sealed partial class LibraryStore
{
    /// <summary>
    /// Makes an unconfigured workflow the one for a manager library: named after it, linked to it, and given its folders
    /// (a null folder is left as it is). Everything else about the workflow stays as it was.
    /// </summary>
    public async Task<ProcessingLibraryRecord> AdoptForManagerAsync(
        UnitOfWork uow, ProcessingLibraryRecord existing, string name, long connectionId, string libraryKey, string? watchedFolder, string? outputFolder)
    {
        await uow.ExecuteAsync(
            "UPDATE libraries SET name = @name, watched_folder = @watched, output_folder = @output, " +
            "discovered_from_connection_id = @connection, discovered_library_key = @key, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@name", name),
            ("@watched", watchedFolder ?? existing.WatchedFolder),
            ("@output", outputFolder ?? existing.OutputFolder),
            ("@connection", connectionId),
            ("@key", libraryKey),
            ("@id", existing.Id)).ConfigureAwait(false);
        Changed(uow);
        var linked = await ManagerConnectionIdsAsync(uow, existing.Id).ConfigureAwait(false);
        await SetManagerLinksAsync(uow, existing.Id, [.. linked.Append(connectionId).Distinct()]).ConfigureAwait(false);
        return await GetAsync(uow, existing.Id).ConfigureAwait(false) ?? throw new InvalidOperationException("Workflow disappeared during setup from a manager.");
    }

    /// <summary>Rewrites a workflow's watched and output folders to what its manager reports (a null folder is left as it is).</summary>
    public async Task<ProcessingLibraryRecord> SetFoldersFromManagerAsync(
        UnitOfWork uow, ProcessingLibraryRecord existing, string? watchedFolder, string? outputFolder)
    {
        await uow.ExecuteAsync(
            "UPDATE libraries SET watched_folder = @watched, output_folder = @output, updated_at = CURRENT_TIMESTAMP WHERE id = @id",
            ("@watched", watchedFolder ?? existing.WatchedFolder),
            ("@output", outputFolder ?? existing.OutputFolder),
            ("@id", existing.Id)).ConfigureAwait(false);
        Changed(uow);
        return await GetAsync(uow, existing.Id).ConfigureAwait(false) ?? throw new InvalidOperationException("Workflow disappeared during folder update.");
    }
}
