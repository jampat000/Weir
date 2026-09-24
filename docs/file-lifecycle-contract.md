# File lifecycle contract

Weir must never report a partial media mutation as successful, and must never expose a partial file at a final path.

## Required mutation pattern

- Write or copy into a staged file first.
- Validate the staged file before final placement where validation is available.
- Expose the final path only through a rename within the destination directory, so the final name only ever appears complete.
- For cross-volume placement, copy into a hidden partial file in the destination directory first, then rename it onto the final path. A plain `File.Move` must not be used across volumes: .NET silently turns it into a copy to the final name, which exposes a half-written file.
- Only record job or History success after the final file exists and passes the relevant safety checks.
- Do not delete watched-folder or output-folder material unless the operation has traceable intent and an output safety check has passed.

## Seams

Server code places final media files through these seams only, never through direct final-path copies, direct cross-volume moves or delete-then-copy patterns.

### Processing output: `Weir.Infrastructure.Processing.RemuxPass.FileLifecycle`

- `SafeCopyToFinalAsync` copies the source into a hidden `.{name}.XXXXXXXX.partial` file beside the destination, runs the optional staged-file validation, then renames the partial onto the destination. A failed or cancelled copy, or a failed validation, deletes the partial and leaves the destination untouched.
- `TryHardlinkToFinalAsync` is the same-volume fast path: it creates a hidden `.{name}.XXXXXXXX.link` hard link beside the destination, validates it and renames it onto the destination. It returns false when the link is refused, and the caller falls back to `SafeCopyToFinalAsync`.
- `SafeFinalizeFile` publishes a file Weir has already written in its work folder: the staged file is moved to a hidden `.partial` beside the destination (copied instead if it cannot be moved), then renamed onto the destination. On the same volume this is two renames and no copy.

Remux pass publishing and pass-through delivery both go through these methods. Failures raise `FileLifecycleException` after removing the partial.

### Library mode: `Weir.Infrastructure.LibraryMode.SafeSwap`

`SafeSwap` replaces a file inside a library with its cleaned copy so that a crash, power cut, locked file or concurrent change can never lose the file or overwrite a newer one:

1. Leftovers of an earlier interrupted swap of the same file are put right first.
2. Preflight refuses the swap when the file is missing, hardlinked (unless the library allows it), short of free space (file size plus 1 GiB), in a folder that is not writable, read-only, or in use.
3. The cleaned copy is written as `<name>.weir-tmp<ext>` beside the original and validated.
4. The original is fingerprinted again. If it changed, the copy is discarded and nothing is replaced.
5. The original is renamed to `<name>.weir-bak<ext>`, then the copy is renamed to the original name. That second rename is the commit.
6. The backup is deleted. A backup that cannot be deleted is retried by the startup sweep.

Every rename is a same-directory rename that never overwrites: on Windows `MoveFileExW` is called without `MOVEFILE_REPLACE_EXISTING` or `MOVEFILE_COPY_ALLOWED`, and elsewhere `File.Move` is called without overwrite. A file that appears at the destination while Weir works (for example, a media manager importing a newer copy) is never replaced, and a rename can never silently become a copy.

Each step is journalled. A failure before the commit rolls back from the files on disk: the backup is renamed back and the temporary copy is deleted. A crash skips the rollback, and `SwapRecoverySweep` applies the same rules at the next start. Either way exactly one intact file is left under the original name.

A file held by another program is not a failure: the swap reports it as in use and the job is requeued.

## Output ownership

When the optional ownership settings (`WEIR_CHOWN_OUTPUT`, `WEIR_FILE_MODE_OUTPUT`, `WEIR_DIR_MODE_OUTPUT`) are set on Linux, they are applied at these same publish points: the three `FileLifecycle` methods and the `SafeSwap` commit. A failure to apply them never fails the job.

## Deletion rules

- Missing files are already absent, not success with hidden work.
- Locked or in-use files must produce an operator-readable skipped or failed reason.
