import { useEffect, useState } from "react";

import type { LibrarySettings } from "../../../../lib/processing/library-mode-api";
import {
  useLibrarySettingsQuery,
  useSaveLibraryPreflightSettings,
  useSetLibrarySchedule,
} from "../../../../lib/processing/library-mode-queries";

/** "Files already in your library" as the library editor holds it until Save. */
export type CleaningDraft = Pick<
  LibrarySettings,
  | "library_folders"
  | "clean_hardlinked_files"
  | "skip_if_manager_would_redownload"
  | "library_schedule_enabled"
  | "keep_original_after_clean"
  | "originals_folder"
>;

function draftOf(settings: LibrarySettings): CleaningDraft {
  return {
    library_folders: settings.library_folders,
    clean_hardlinked_files: settings.clean_hardlinked_files,
    skip_if_manager_would_redownload: settings.skip_if_manager_would_redownload,
    library_schedule_enabled: settings.library_schedule_enabled,
    keep_original_after_clean: settings.keep_original_after_clean,
    originals_folder: settings.originals_folder,
  };
}

function sameFolders(a: readonly string[], b: readonly string[]): boolean {
  return a.length === b.length && a.every((folder, i) => folder === b[i]);
}

function checksChanged(draft: CleaningDraft, saved: CleaningDraft): boolean {
  return (
    !sameFolders(draft.library_folders, saved.library_folders) ||
    draft.clean_hardlinked_files !== saved.clean_hardlinked_files ||
    draft.skip_if_manager_would_redownload !==
      saved.skip_if_manager_would_redownload ||
    draft.keep_original_after_clean !== saved.keep_original_after_clean ||
    draft.originals_folder !== saved.originals_folder
  );
}

/**
 * One saved library's existing-files settings, held as a draft so the editor's Save and Cancel cover
 * them like every other field. A library still being added has none (`libraryId` undefined).
 */
export function useLibraryCleaningDraft(libraryId: number | undefined) {
  const id = libraryId ?? 0;
  const settings = useLibrarySettingsQuery(id, libraryId !== undefined);
  const saveChecks = useSaveLibraryPreflightSettings(id);
  const setSchedule = useSetLibrarySchedule(id);
  const [draft, setDraft] = useState<CleaningDraft | null>(null);

  // The draft starts from the first answer; later refetches do not overwrite someone's edits.
  useEffect(() => {
    if (settings.data && draft === null) setDraft(draftOf(settings.data));
  }, [settings.data, draft]);

  const saved = settings.data ? draftOf(settings.data) : null;
  const dirty =
    draft !== null &&
    saved !== null &&
    (checksChanged(draft, saved) ||
      draft.library_schedule_enabled !== saved.library_schedule_enabled);

  /**
   * Folders and checks first, because the daily clean needs folders to switch on. The daily clean is
   * only ever switched on here after its warning was accepted, so it goes with its confirmation.
   */
  const save = async (): Promise<void> => {
    if (draft === null || saved === null || !dirty) return;
    if (checksChanged(draft, saved)) {
      await saveChecks.mutateAsync({
        library_folders: draft.library_folders,
        clean_hardlinked_files: draft.clean_hardlinked_files,
        skip_if_manager_would_redownload:
          draft.skip_if_manager_would_redownload,
        keep_original_after_clean: draft.keep_original_after_clean,
        originals_folder: draft.originals_folder,
      });
    }
    if (draft.library_schedule_enabled !== saved.library_schedule_enabled) {
      const result = await setSchedule.mutateAsync({
        enabled: draft.library_schedule_enabled,
        confirm: draft.library_schedule_enabled,
      });
      if ("kind" in result) throw new Error(result.detail);
    }
  };

  return {
    failed: settings.isError,
    draft,
    dirty,
    change: (patch: Partial<CleaningDraft>) =>
      setDraft((current) => (current ? { ...current, ...patch } : current)),
    save,
  };
}

export type LibraryCleaningDraft = ReturnType<typeof useLibraryCleaningDraft>;
