/**
 * How the connected media manager picks up what a library writes, inside the library editor. Deluno only
 * needs the right folders, which Weir reads from it and offers to fill in. Sonarr and Radarr need a remote
 * path mapping from the watched folder to the output folder; this shows what to type and checks it. The
 * check only ever sends GET requests and never changes a manager's settings.
 */

import { useState } from "react";
import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import {
  PROCESSING_MEDIA_TYPE_LABELS,
  type ProcessingMediaType,
} from "../../../../lib/processing/libraries-api";
import { type ProcessingManagerSetupItem } from "../../../../lib/processing/library-managers-api";
import { useProcessingManagerSetupQuery } from "../../../../lib/processing/libraries-queries";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";

/** The folders as the user types them settle for a moment before each check. */
const SETTLE_MS = 700;

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

function CheckLines({ item }: { item: ProcessingManagerSetupItem }) {
  return (
    <ul
      className="space-y-1.5 text-sm leading-5"
      aria-label={`${item.label} check`}
    >
      {item.lines.map((line, index) => (
        <li key={index} className="flex gap-2">
          <span
            aria-hidden="true"
            className={
              line.state === "ok"
                ? "mm-status-text--healthy"
                : line.state === "problem"
                  ? "mm-status-text--warning"
                  : "text-mm-text3"
            }
          >
            {line.state === "ok" ? "✓" : line.state === "problem" ? "✗" : "·"}
          </span>
          <span
            className={
              line.state === "problem"
                ? "mm-status-text--warning"
                : "text-mm-text2"
            }
          >
            <span className="sr-only">
              {line.state === "ok"
                ? "Fine: "
                : line.state === "problem"
                  ? "Needs a fix: "
                  : "Note: "}
            </span>
            {line.text}
          </span>
        </li>
      ))}
    </ul>
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
      shown: mapping.remote_path || "this library's watched folder",
    },
    {
      field: "Local Path",
      value: mapping.local_path,
      shown: mapping.local_path || "this library's output folder",
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
      <div className="mm-quiet-table-wrap">
        <table className="mm-quiet-table">
          <thead>
            <tr>
              <th scope="col">Field</th>
              <th scope="col">Value</th>
              <th scope="col">
                <span className="sr-only">Copy</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {rows.map((row) => (
              <tr key={row.field}>
                <th scope="row" className="mm-quiet-table__name">
                  <span>{row.field}</span>
                </th>
                <td data-label="Value">
                  <code className="break-all">{row.shown}</code>
                </td>
                <td data-label="Copy">
                  <CopyLink
                    value={row.value}
                    label={`${item.label} ${row.field}`}
                  />
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
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
      <p className="mm-quiet-note">
        {item.label} hands each finished download to Weir and imports the
        cleaned file back by itself, so there is nothing to map. This library
        needs the folder {item.label}&apos;s downloads arrive in as its watched
        folder, and the folder {item.label} picks cleaned files up from as its
        output folder.
      </p>
      {watched !== null || output !== null ? (
        <p className="text-sm text-mm-text2">
          {item.label} reports downloads in{" "}
          <code className="break-all">{watched ?? "(not set)"}</code> and picks
          up cleaned files from{" "}
          <code className="break-all">{output ?? "(not set)"}</code>.{" "}
          {differs && editable ? (
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
  removeOriginal = true,
  editable,
  onUseFolders,
}: {
  mediaType: ProcessingMediaType;
  watchedFolder: string;
  outputFolder: string;
  /** The library's "After cleaning, remove the original download": a torrent client makes that a problem. */
  removeOriginal?: boolean;
  editable: boolean;
  onUseFolders: (watched: string | null, output: string | null) => void;
}) {
  const settled = useDebouncedValue(
    { mediaType, watched: watchedFolder.trim(), output: outputFolder.trim() },
    SETTLE_MS,
  );
  const setup = useProcessingManagerSetupQuery(
    settled.mediaType,
    settled.watched,
    settled.output,
    removeOriginal,
    true,
  );
  const scope = PROCESSING_MEDIA_TYPE_LABELS[mediaType];
  const managers = setup.data?.managers ?? [];

  return (
    <QuietFieldGroup
      title="Media manager"
      detail="How your media manager picks up the files this library writes."
      aside={
        managers.length > 0 ? (
          <button
            type="button"
            className="mm-quiet-link"
            onClick={() => void setup.refetch()}
            disabled={setup.isFetching}
          >
            {setup.isFetching ? "Checking…" : "Check again"}
          </button>
        ) : null
      }
    >
      <div data-testid="library-manager-setup" className="space-y-6">
        {setup.isLoading ? (
          <p className="mm-quiet-note">Checking your media managers…</p>
        ) : setup.isError ? (
          <p className="mm-quiet-note mm-status-text--warning" role="alert">
            Weir could not check your media managers just now. Try Check again
            in a moment.
          </p>
        ) : managers.length === 0 ? (
          <p className="mm-quiet-note">
            No Sonarr, Radarr or Deluno connection covers {scope}. Connect one
            under Settings → Media managers and this shows exactly how to hand
            it the files this library writes.
          </p>
        ) : (
          managers.map((item) => (
            <section
              key={item.connection_id}
              aria-label={item.label}
              className="space-y-3"
            >
              <p className="text-sm font-medium text-mm-text1">
                {item.label}
                <span
                  className={`ml-2 text-xs ${
                    item.ready
                      ? "mm-status-text--healthy"
                      : "mm-status-text--warning"
                  }`}
                >
                  {item.ready ? "Ready" : "Needs attention"}
                </span>
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
              <CheckLines item={item} />
            </section>
          ))
        )}
      </div>
    </QuietFieldGroup>
  );
}
