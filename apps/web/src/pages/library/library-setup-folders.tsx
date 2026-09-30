import { useState } from "react";

import { ServerFolderPickerButton } from "../../components/ui/server-folder-picker-button";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import { examplePath } from "../../lib/ui/platform";

/** The folders a library reads, one per row, each removable while the setup can be edited. */
export function FolderList({
  folders,
  editable,
  onRemove,
}: {
  folders: string[];
  editable: boolean;
  onRemove: (folder: string) => void;
}) {
  return (
    <ul className="mm-library-setup__folders" aria-label="Library folders">
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
        <li className="mm-library-setup__empty">
          No folders yet, so there is nothing to show for this library.
        </li>
      ) : null}
    </ul>
  );
}

export function AddFolder({ onAdd }: { onAdd: (folder: string) => void }) {
  const [newFolder, setNewFolder] = useState("");
  const add = (folder: string) => {
    const trimmed = folder.trim();
    if (!trimmed) return;
    onAdd(trimmed);
    setNewFolder("");
  };
  return (
    <div className="mm-library-setup__add">
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
