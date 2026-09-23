import { useEffect, useId, useState } from "react";

import { ServerFolderPickerButton } from "../../../../components/ui/server-folder-picker-button";
import type { ProcessingRuleSetWrite } from "../../../../lib/processing/rule-sets-api";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";
import {
  mmActionButtonClass,
  mmEditableTextFieldClass,
  mmSelectFieldClass,
} from "../../../../lib/ui/mm-control-roles";
import { examplePath } from "../../../../lib/ui/platform";
import { RulesPreviewResults } from "./rules-preview-results";
import { useRulesPreview, type PreviewTarget } from "./use-rules-preview";

type PathMode = PreviewTarget["mode"];

function joinPath(folder: string, fileName: string): string {
  const trimmedFolder = folder.trim().replace(/[/\\]+$/, "");
  const trimmedFile = fileName.trim().replace(/^[/\\]+/, "");
  if (!trimmedFolder) return trimmedFile;
  if (!trimmedFile) return trimmedFolder;
  const separator = trimmedFolder.includes("\\") ? "\\" : "/";
  return `${trimmedFolder}${separator}${trimmedFile}`;
}

function PanelHead({
  headingId,
  lead,
  action,
  open,
}: {
  headingId?: string;
  lead: string;
  action: React.ReactNode;
  open: boolean;
}) {
  return (
    <div
      className={`mm-rules-preview__head${open ? " mm-rules-preview__head--open" : ""}`}
    >
      <div>
        <h3 id={headingId} className="mm-rules-preview__title">
          Try on a file
        </h3>
        <p className="mm-rules-preview__lead">{lead}</p>
      </div>
      {action}
    </div>
  );
}

function FileAnywhereFields({
  folder,
  fileName,
  onFolder,
  onFileName,
}: {
  folder: string;
  fileName: string;
  onFolder: (value: string) => void;
  onFileName: (value: string) => void;
}) {
  return (
    <div className="mm-rules-preview__folder">
      <label className="mm-rules-preview__field">
        <span className="mm-rules-preview__label">Folder</span>
        <input
          className={mmEditableTextFieldClass}
          value={folder}
          placeholder={examplePath(
            String.raw`X:\Media\Movies\Movie (2020)`,
            "/media/movies/Movie (2020)",
          )}
          onChange={(event) => onFolder(event.target.value)}
        />
      </label>
      <ServerFolderPickerButton
        title="Choose the file's folder"
        value={folder}
        onSelect={onFolder}
      />
      <label className="mm-rules-preview__field mm-rules-preview__field--wide">
        <span className="mm-rules-preview__label">File name</span>
        <input
          className={mmEditableTextFieldClass}
          value={fileName}
          placeholder="Movie.mkv"
          onChange={(event) => onFileName(event.target.value)}
        />
      </label>
    </div>
  );
}

/**
 * The rules tried on a real file, read-only: nothing is processed, queued or saved. It shows the
 * editor's current draft, so the result changes as the rules above do.
 */
export function RulesPreviewPanel({
  rules,
  disabled,
}: {
  rules: ProcessingRuleSetWrite | null;
  disabled?: boolean;
}) {
  const libraries = useProcessingLibrariesQuery();
  const [open, setOpen] = useState(false);
  const [libraryId, setLibraryId] = useState<number | "">("");
  const [pathMode, setPathMode] = useState<PathMode>("library");
  const [relativePath, setRelativePath] = useState("");
  const [folder, setFolder] = useState("");
  const [fileName, setFileName] = useState("");
  const headingId = useId();
  const target: PreviewTarget =
    pathMode === "library"
      ? { mode: "library", relativePath }
      : { mode: "anywhere", absolutePath: joinPath(folder, fileName) };
  const preview = useRulesPreview({ active: open, libraryId, target, rules });

  // The first library is the one to try against until another is chosen.
  useEffect(() => {
    if (libraryId !== "" || !libraries.data?.length) return;
    setLibraryId(libraries.data[0].id);
  }, [libraries.data, libraryId]);

  if (!open) {
    return (
      <section>
        <PanelHead
          open={false}
          lead="See exactly what these rules would keep or drop on a real file — nothing is processed or queued."
          action={
            <button
              type="button"
              className={mmActionButtonClass({ variant: "secondary" })}
              disabled={disabled}
              onClick={() => setOpen(true)}
            >
              Try on a file
            </button>
          }
        />
      </section>
    );
  }

  return (
    <section className="space-y-4" aria-labelledby={headingId}>
      <PanelHead
        open
        headingId={headingId}
        lead="Read-only: this never processes, queues, or saves anything. It re-runs a moment after you stop editing the rules above."
        action={
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            onClick={() => setOpen(false)}
          >
            Close
          </button>
        }
      />

      <div className="mm-rules-preview__choices">
        <label className="mm-rules-preview__field">
          <span className="mm-rules-preview__label">Library</span>
          <select
            className={mmSelectFieldClass}
            value={libraryId}
            onChange={(event) => setLibraryId(Number(event.target.value))}
          >
            {(libraries.data ?? []).map((library) => (
              <option key={library.id} value={library.id}>
                {library.name}
              </option>
            ))}
          </select>
          <span className="mm-rules-preview__hint">
            Decides which watched/output folders a relative path may resolve
            against.
          </span>
        </label>
        <label className="mm-rules-preview__field">
          <span className="mm-rules-preview__label">Where is the file?</span>
          <select
            className={mmSelectFieldClass}
            value={pathMode}
            onChange={(event) =>
              setPathMode(
                event.target.value === "anywhere" ? "anywhere" : "library",
              )
            }
          >
            <option value="library">
              Inside the library&apos;s watched or output folder
            </option>
            <option value="anywhere">Anywhere on this machine</option>
          </select>
        </label>
      </div>

      {pathMode === "library" ? (
        <label className="mm-rules-preview__field">
          <span className="mm-rules-preview__label">
            Path within the watched or output folder
          </span>
          <input
            className={mmEditableTextFieldClass}
            value={relativePath}
            placeholder="Movie (2020)/Movie.mkv"
            onChange={(event) => setRelativePath(event.target.value)}
          />
        </label>
      ) : (
        <FileAnywhereFields
          folder={folder}
          fileName={fileName}
          onFolder={setFolder}
          onFileName={setFileName}
        />
      )}

      <div className="flex flex-wrap items-center gap-2">
        <button
          type="button"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={!preview.runNow || preview.running}
          onClick={() => preview.runNow?.()}
        >
          {preview.running ? "Checking…" : "Preview"}
        </button>
        {preview.running ? (
          <span className="text-xs text-mm-text3" role="status">
            Reading the file and running the rules…
          </span>
        ) : null}
      </div>

      <div aria-live="polite">
        {preview.error ? (
          <p
            role="alert"
            className="text-sm font-medium text-mm-status-failed-text"
          >
            {preview.error}
          </p>
        ) : null}
        {!preview.error && preview.result ? (
          <RulesPreviewResults result={preview.result} />
        ) : null}
      </div>
    </section>
  );
}
