/**
 * What the media managers a workflow is linked to need to pick up what it writes, inside the workflow editor.
 * Deluno only needs the right folders, which Weir reads from it and offers to fill in. Sonarr and Radarr need a
 * remote path mapping from the watched folder to the output folder; this shows what to type. Whether it is
 * right is the folder chain's to say, once, in its own section. Reading only ever sends GET requests and never
 * changes a manager's settings.
 */

import { useState } from "react";
import { EditorSubsection } from "./editor-subsection";
import { type ProcessingMediaType } from "../../../../lib/processing/libraries-api";
import { type ProcessingManagerSetupItem } from "../../../../lib/processing/library-managers-api";
import { useProcessingManagerSetupQuery } from "../../../../lib/processing/libraries-queries";
import { workflowStory } from "../../../../lib/processing/workflow-story";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";
import { FOLDER_CHECK_SETTLE_MS } from "./folder-check-settle";

function CopyLink({ value, label }: { value: string; label: string }) {
  const [copied, setCopied] = useState(false);
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2000);
    } catch {
      setCopied(false);
    }
  };
  return (
    <button
      type="button"
      className="mm-quiet-link"
      onClick={() => void copy()}
      disabled={!value}
      aria-label={`Copy ${label}`}
    >
      {copied ? "Copied" : "Copy"}
    </button>
  );
}

function ArrSuggestedFolder({
  item,
  watchedFolder,
  editable,
  onUseFolders,
}: {
  item: ProcessingManagerSetupItem;
  watchedFolder: string;
  editable: boolean;
  onUseFolders: (watched: string | null, output: string | null) => void;
}) {
  const suggested = item.suggested_watched_folder ?? null;
  if (suggested === null || suggested === watchedFolder.trim()) return null;
  return (
    <p className="text-sm text-mm-text2">
      {item.label} has a download client saving to{" "}
      <code className="break-all">{suggested}</code>.{" "}
      {editable ? (
        <button
          type="button"
          className="mm-quiet-link"
          onClick={() => onUseFolders(suggested, null)}
        >
          Use this as the watched folder
        </button>
      ) : null}
    </p>
  );
}

function ArrMapping({
  item,
  watchedFolder,
  editable,
  onUseFolders,
}: {
  item: ProcessingManagerSetupItem;
  watchedFolder: string;
  editable: boolean;
  onUseFolders: (watched: string | null, output: string | null) => void;
}) {
  const mapping = item.mapping;
  if (!mapping) return null;
  const host = mapping.hosts.join(" or ");
  const rows: { field: string; value: string; shown: string }[] = [
    {
      field: "Host",
      value: mapping.hosts[0] ?? "",
      shown:
        host ||
        `your download client's Host, exactly as it is entered in ${item.label}`,
    },
    {
      field: "Remote Path",
      value: mapping.remote_path,
      shown: mapping.remote_path || "this workflow's watched folder",
    },
    {
      field: "Local Path",
      value: mapping.local_path,
      shown: mapping.local_path || "this workflow's output folder",
    },
  ];
  return (
    <>
      <ArrSuggestedFolder
        item={item}
        watchedFolder={watchedFolder}
        editable={editable}
        onUseFolders={onUseFolders}
      />
      <p className="mm-quiet-note">
        {item.label} imports what Weir writes by looking in Weir&apos;s output
        folder instead of the download client&apos;s. In {item.label}, open
        Settings → Download Clients → Remote Path Mappings, add a mapping with
        these values, and keep Completed Download Handling on.
      </p>
      <table className="mm-copy-table">
        <thead className="sr-only">
          <tr>
            <th scope="col">Field</th>
            <th scope="col">Value</th>
            <th scope="col">Copy</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.field}>
              <th scope="row">{row.field}</th>
              <td>
                <code>{row.shown}</code>
              </td>
              <td>
                <CopyLink
                  value={row.value}
                  label={`${item.label} ${row.field}`}
                />
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {mapping.hosts.length > 1 ? (
        <p className="mm-quiet-note">
          {item.label} has more than one download client. Add the same mapping
          once for each Host: {host}.
        </p>
      ) : null}
      <p className="text-xs leading-5 text-mm-text3">
        These are the folders as Weir sees them. If {item.label} or your
        download client runs in its own container with different volume paths,
        use the paths each of them sees for the same folders.
      </p>
    </>
  );
}

function DelunoHandoff({
  item,
  watchedFolder,
  outputFolder,
  editable,
  onUseFolders,
}: {
  item: ProcessingManagerSetupItem;
  watchedFolder: string;
  outputFolder: string;
  editable: boolean;
  onUseFolders: (watched: string | null, output: string | null) => void;
}) {
  const watched = item.suggested_watched_folder ?? null;
  const output = item.suggested_output_folder ?? null;
  const differs =
    (watched !== null && watched !== watchedFolder.trim()) ||
    (output !== null && output !== outputFolder.trim());
  return (
    <>
      {differs ? (
        <p className="text-sm text-mm-text2">
          {item.label} reports downloads in{" "}
          <code className="break-all">{watched ?? "(not set)"}</code> and picks
          up cleaned files from{" "}
          <code className="break-all">{output ?? "(not set)"}</code>.{" "}
          {editable ? (
            <button
              type="button"
              className="mm-quiet-link"
              onClick={() => onUseFolders(watched, output)}
            >
              Use {item.label}&apos;s folders
            </button>
          ) : null}
        </p>
      ) : null}
    </>
  );
}

export function LibraryManagerSetup({
  mediaType,
  watchedFolder,
  outputFolder,
  workFolder,
  removeOriginal = true,
  linkedConnectionIds,
  editable,
  onUseFolders,
}: {
  mediaType: ProcessingMediaType;
  watchedFolder: string;
  outputFolder: string;
  workFolder: string;
  /** The workflow's "New downloads: after cleaning, delete the original download": a torrent client makes that a problem. */
  removeOriginal?: boolean;
  /** The media managers this workflow is linked to: only these are read. */
  linkedConnectionIds: number[];
  editable: boolean;
  onUseFolders: (watched: string | null, output: string | null) => void;
}) {
  const settled = useDebouncedValue(
    { mediaType, watched: watchedFolder.trim(), output: outputFolder.trim() },
    FOLDER_CHECK_SETTLE_MS,
  );
  const setup = useProcessingManagerSetupQuery(
    settled.mediaType,
    settled.watched,
    settled.output,
    removeOriginal,
    linkedConnectionIds,
    linkedConnectionIds.length > 0,
  );
  const managers = setup.data?.managers ?? [];

  return (
    <EditorSubsection
      title="What your media manager needs"
      detail="The folders and settings the media manager has to hold to pick up what Weir writes."
    >
      <div data-testid="library-manager-setup" className="space-y-6">
        {setup.isLoading ? (
          <p className="mm-quiet-note">Reading your media managers…</p>
        ) : setup.isError ? (
          <p className="mm-quiet-note mm-status-text--warning" role="alert">
            Weir could not read your media managers just now. Reopen this editor
            in a moment.
          </p>
        ) : (
          managers.map((item) => (
            <section
              key={item.connection_id}
              aria-label={item.label}
              className="space-y-3"
            >
              <p className="text-sm font-medium text-mm-text1">{item.label}</p>
              <p className="mm-quiet-note" data-testid="workflow-story">
                {workflowStory(
                  {
                    watched: watchedFolder.trim(),
                    work: workFolder.trim(),
                    output: outputFolder.trim(),
                  },
                  { id: item.connection_id, name: item.label, kind: item.kind },
                  {
                    category: item.story?.source_category ?? null,
                    managerLibrary: item.story?.manager_library ?? null,
                    rootFolder: item.story?.root_folder ?? null,
                  },
                )}
              </p>
              {item.flow === "handoff" ? (
                <DelunoHandoff
                  item={item}
                  watchedFolder={watchedFolder}
                  outputFolder={outputFolder}
                  editable={editable}
                  onUseFolders={onUseFolders}
                />
              ) : (
                <ArrMapping
                  item={item}
                  watchedFolder={watchedFolder}
                  editable={editable}
                  onUseFolders={onUseFolders}
                />
              )}
            </section>
          ))
        )}
      </div>
    </EditorSubsection>
  );
}
