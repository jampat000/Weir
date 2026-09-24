import { useState } from "react";

import { MmOnOffSwitch } from "../../../../components/ui/mm-on-off-switch";
import { ServerFolderPickerButton } from "../../../../components/ui/server-folder-picker-button";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { originalsFolderLabel } from "../../../../lib/processing/library-mode-api";
import { examplePath } from "../../../../lib/ui/platform";
import type { LibraryCleaningDraft } from "./library-cleaning-draft";

/** #735: the switch and folder field for keeping a clean's pre-clean original instead of deleting it. */
function KeepOriginalSettings({
  libraryId,
  enabled,
  originalsFolder,
  editable,
  onChangeEnabled,
  onChangeFolder,
}: {
  libraryId: number;
  enabled: boolean;
  originalsFolder: string;
  editable: boolean;
  onChangeEnabled: (next: boolean) => void;
  onChangeFolder: (folder: string) => void;
}) {
  return (
    <>
      <MmOnOffSwitch
        id={`library-${libraryId}-keep-original`}
        label="Keep the original after cleaning"
        enabled={enabled}
        disabled={!editable}
        onChange={onChangeEnabled}
      />
      {enabled ? (
        <div className="mm-library-cleaning__originals-folder">
          <p className="mm-library-cleaning__hint">
            Weir moves the original into {originalsFolderLabel(originalsFolder)}{" "}
            instead of deleting it, so removed tracks can be recovered.
            You&rsquo;ll need the disk space for both.
          </p>
          <input
            className="mm-input"
            placeholder={examplePath(
              String.raw`D:\Media\Movies\.weir-originals`,
              "/media/movies/.weir-originals",
            )}
            aria-label="Originals folder"
            value={originalsFolder}
            disabled={!editable}
            onChange={(event) => onChangeFolder(event.target.value)}
          />
          <ServerFolderPickerButton
            title="Choose where kept originals go"
            value={originalsFolder}
            onSelect={onChangeFolder}
          />
        </div>
      ) : null}
    </>
  );
}

function FolderList({
  folders,
  editable,
  onRemove,
}: {
  folders: string[];
  editable: boolean;
  onRemove: (folder: string) => void;
}) {
  return (
    <ul className="mm-library-cleaning__folders" aria-label="Library folders">
      {folders.map((folder) => (
        <li key={folder}>
          <code>{folder}</code>
          {editable ? (
            <button
              type="button"
              className={mmActionButtonClass({ variant: "tertiary" })}
              aria-label={`Remove ${folder}`}
              onClick={() => onRemove(folder)}
            >
              Remove
            </button>
          ) : null}
        </li>
      ))}
      {folders.length === 0 ? (
        <li className="mm-library-cleaning__empty">
          No folders yet, so the Library screen has nothing to show for this
          library.
        </li>
      ) : null}
    </ul>
  );
}

function AddFolder({ onAdd }: { onAdd: (folder: string) => void }) {
  const [newFolder, setNewFolder] = useState("");
  const add = (folder: string) => {
    const trimmed = folder.trim();
    if (!trimmed) return;
    onAdd(trimmed);
    setNewFolder("");
  };
  return (
    <div className="mm-library-cleaning__add">
      <input
        className="mm-input"
        placeholder={examplePath(String.raw`D:\Media\Movies`, "/media/movies")}
        aria-label="Folder to add"
        value={newFolder}
        onChange={(event) => setNewFolder(event.target.value)}
        onKeyDown={(event) => {
          if (event.key === "Enter") {
            event.preventDefault();
            add(newFolder);
          }
        }}
      />
      <ServerFolderPickerButton
        title="Choose a folder your files sit in"
        value={newFolder}
        onSelect={add}
      />
      <button
        type="button"
        className={mmActionButtonClass({ variant: "secondary" })}
        disabled={newFolder.trim() === ""}
        onClick={() => add(newFolder)}
      >
        Add folder
      </button>
    </div>
  );
}

/** Switching the daily clean on always asks first, because it removes tracks without asking each time. */
function DailyCleanWarning({
  onAccept,
  onCancel,
}: {
  onAccept: () => void;
  onCancel: () => void;
}) {
  return (
    <div
      className="mm-library-cleaning__confirm"
      role="alertdialog"
      aria-label="Switch on the daily clean"
    >
      <p>
        Once a day Weir will check these folders and remove the tracks this
        library&apos;s rules remove, from every file that has them, without
        asking each time. Removed tracks cannot be put back. Files you have left
        alone are never touched.
      </p>
      <div className="mm-library-cleaning__confirm-actions">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          onClick={onAccept}
        >
          Switch it on
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          onClick={onCancel}
        >
          Cancel
        </button>
      </div>
    </div>
  );
}

/**
 * The files already on your storage, for one library: the folders the Library screen reads, the two
 * safety checks a clean makes, and the once-a-day check and clean. They sit in the library's own
 * editor and follow its model: nothing here is saved until the editor's Save.
 */
export function LibraryCleaningSettings({
  libraryId,
  cleaning,
  editable,
}: {
  libraryId: number;
  cleaning: LibraryCleaningDraft;
  editable: boolean;
}) {
  const [warning, setWarning] = useState(false);
  const { draft, change } = cleaning;

  if (cleaning.failed) {
    return (
      <p className="mm-status-text--failed text-sm" role="alert">
        Weir couldn&rsquo;t load these settings. Reload the page to try again.
      </p>
    );
  }
  if (draft === null) {
    return <p className="mm-quiet-note">Loading…</p>;
  }
  const folders = draft.library_folders;

  return (
    <div
      className="mm-library-cleaning"
      data-testid="library-cleaning-settings"
    >
      <p className="mm-quiet-note">
        Where the files you already have sit. The Library screen reads them
        here, and Weir only changes one when you ask, or once a day if you
        switch that on below.
      </p>

      <FolderList
        folders={folders}
        editable={editable}
        onRemove={(folder) =>
          change({ library_folders: folders.filter((f) => f !== folder) })
        }
      />
      {editable ? (
        <AddFolder
          onAdd={(folder) =>
            change({
              library_folders: Array.from(new Set([...folders, folder])),
            })
          }
        />
      ) : null}

      <div className="mm-library-cleaning__switches">
        <MmOnOffSwitch
          id={`library-${libraryId}-daily-clean`}
          label="Check and clean once a day, inside this library's hours"
          enabled={draft.library_schedule_enabled}
          disabled={!editable || folders.length === 0}
          onChange={(next) =>
            next
              ? setWarning(true)
              : change({ library_schedule_enabled: false })
          }
        />
        {warning ? (
          <DailyCleanWarning
            onAccept={() => {
              setWarning(false);
              change({ library_schedule_enabled: true });
            }}
            onCancel={() => setWarning(false)}
          />
        ) : null}
        <MmOnOffSwitch
          id={`library-${libraryId}-skip-redownload`}
          label="Skip a file when cleaning it would make the media manager download it again"
          enabled={draft.skip_if_manager_would_redownload}
          disabled={!editable}
          onChange={(next) =>
            change({ skip_if_manager_would_redownload: next })
          }
        />
        <MmOnOffSwitch
          id={`library-${libraryId}-clean-seeding`}
          label="Clean a file that is still seeding"
          enabled={draft.clean_hardlinked_files}
          disabled={!editable}
          onChange={(next) => change({ clean_hardlinked_files: next })}
        />
        <p className="mm-library-cleaning__hint">
          Off by default: a file that is still seeding shares its data with the
          download, so cleaning it frees no space.
        </p>
        <KeepOriginalSettings
          libraryId={libraryId}
          enabled={draft.keep_original_after_clean}
          originalsFolder={draft.originals_folder}
          editable={editable}
          onChangeEnabled={(next) =>
            change({ keep_original_after_clean: next })
          }
          onChangeFolder={(folder) => change({ originals_folder: folder })}
        />
      </div>
    </div>
  );
}
