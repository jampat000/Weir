import { useState } from "react";

import { MmOnOffSwitch } from "../../../components/ui/mm-on-off-switch";
import {
  useLibrarySettingsQuery,
  useSaveLibraryFolders,
  useSaveLibraryPreflightSettings,
  useSetLibrarySchedule,
} from "../../../lib/processing/library-queries";
import { mmActionButtonClass } from "../../../lib/ui/mm-control-roles";

function message(error: unknown, fallback: string): string {
  return error instanceof Error ? error.message : fallback;
}

/**
 * The files already on your storage, for one library: the folders the Library screen reads, the two safety checks
 * a clean makes, and the once-a-day check and clean. The 3.2 redesign retired the screen these lived on, which left
 * the Library screen telling people to set folders somewhere that no longer offered them. They sit in the library's
 * own editor now, and each saves as soon as it is changed, as it always did.
 */
export function LibraryCleaningSettings({
  libraryId,
  editable,
}: {
  libraryId: number;
  editable: boolean;
}) {
  const settings = useLibrarySettingsQuery(libraryId);
  const saveFolders = useSaveLibraryFolders(libraryId);
  const savePreflight = useSaveLibraryPreflightSettings(libraryId);
  const setSchedule = useSetLibrarySchedule(libraryId);
  const [newFolder, setNewFolder] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [confirming, setConfirming] = useState(false);

  if (settings.isPending) {
    return <p className="mm-quiet-note">Loading…</p>;
  }
  if (settings.isError || !settings.data) {
    return (
      <p className="mm-status-text--failed text-sm" role="alert">
        {message(settings.error, "These settings could not be loaded.")}
      </p>
    );
  }
  const data = settings.data;
  const busy =
    saveFolders.isPending || savePreflight.isPending || setSchedule.isPending;

  const folders = (next: string[]) => {
    setError(null);
    saveFolders.mutate(next, {
      onError: (e) => setError(message(e, "The folders could not be saved.")),
    });
  };

  const add = () => {
    const trimmed = newFolder.trim();
    if (!trimmed) return;
    folders(Array.from(new Set([...data.library_folders, trimmed])));
    setNewFolder("");
  };

  const preflight = (
    field: "clean_hardlinked_files" | "skip_if_manager_would_redownload",
    next: boolean,
  ) => {
    setError(null);
    savePreflight.mutate(
      { library_folders: data.library_folders, [field]: next },
      { onError: (e) => setError(message(e, "That could not be saved.")) },
    );
  };

  // Switching the daily clean on always asks first. The server only asked when the last check had found tracks to
  // remove, so a library that was tidy that day went on to remove tracks later with nobody having said yes to it.
  const schedule = (enabled: boolean) => {
    setError(null);
    setSchedule.mutate(
      { enabled, confirm: enabled },
      {
        onSuccess: () => setConfirming(false),
        onError: (e) =>
          setError(message(e, "The daily check could not be changed.")),
      },
    );
  };

  return (
    <div
      className="mm-library-cleaning"
      data-testid="library-cleaning-settings"
    >
      <p className="mm-quiet-note">
        Where the files you already have sit. The Library screen reads them
        here, and Weir only changes one when you ask, or once a day if you
        switch that on below. These save as soon as you change them.
      </p>

      <ul className="mm-library-cleaning__folders" aria-label="Library folders">
        {data.library_folders.map((folder) => (
          <li key={folder}>
            <code>{folder}</code>
            {editable ? (
              <button
                type="button"
                className={mmActionButtonClass({ variant: "tertiary" })}
                disabled={busy}
                onClick={() =>
                  folders(data.library_folders.filter((f) => f !== folder))
                }
              >
                Remove
              </button>
            ) : null}
          </li>
        ))}
        {data.library_folders.length === 0 ? (
          <li className="mm-library-cleaning__empty">
            No folders yet, so the Library screen has nothing to show for this
            library.
          </li>
        ) : null}
      </ul>

      {editable ? (
        <div className="mm-library-cleaning__add">
          <input
            className="mm-input"
            placeholder="D:\Media\Movies"
            aria-label="Folder to add"
            value={newFolder}
            onChange={(event) => setNewFolder(event.target.value)}
            onKeyDown={(event) => {
              if (event.key === "Enter") {
                event.preventDefault();
                add();
              }
            }}
          />
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={busy || newFolder.trim() === ""}
            onClick={add}
          >
            Add folder
          </button>
        </div>
      ) : null}

      <div className="mm-library-cleaning__switches">
        <MmOnOffSwitch
          id={`library-${libraryId}-daily-clean`}
          label="Check and clean once a day, inside this library's hours"
          enabled={data.library_schedule_enabled}
          disabled={!editable || busy || data.library_folders.length === 0}
          onChange={(next) => (next ? setConfirming(true) : schedule(false))}
        />
        {confirming ? (
          <div
            className="mm-library-cleaning__confirm"
            role="alertdialog"
            aria-label="Switch on the daily clean"
          >
            <p>
              Once a day Weir will check these folders and remove the tracks
              this library&apos;s rules remove, from every file that has them,
              without asking each time. Removed tracks cannot be put back. Files
              you have set aside are never touched.
            </p>
            <div className="mm-library-cleaning__confirm-actions">
              <button
                type="button"
                className={mmActionButtonClass({ variant: "primary" })}
                disabled={busy}
                onClick={() => schedule(true)}
              >
                Switch it on
              </button>
              <button
                type="button"
                className={mmActionButtonClass({ variant: "tertiary" })}
                onClick={() => setConfirming(false)}
              >
                Cancel
              </button>
            </div>
          </div>
        ) : null}
        <MmOnOffSwitch
          id={`library-${libraryId}-skip-redownload`}
          label="Skip a file when cleaning it would make the media manager download it again"
          enabled={data.skip_if_manager_would_redownload}
          disabled={!editable || busy}
          onChange={(next) =>
            preflight("skip_if_manager_would_redownload", next)
          }
        />
        <MmOnOffSwitch
          id={`library-${libraryId}-clean-seeding`}
          label="Clean a file that is still seeding"
          enabled={data.clean_hardlinked_files}
          disabled={!editable || busy}
          onChange={(next) => preflight("clean_hardlinked_files", next)}
        />
        <p className="mm-library-cleaning__hint">
          Off by default: a file that is still seeding shares its data with the
          download, so cleaning it frees no space.
        </p>
      </div>

      {error ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
