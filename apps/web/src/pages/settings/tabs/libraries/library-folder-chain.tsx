/**
 * The "folder chain": whether Weir's own watched, work and output folders line up end to end, plus (folded into
 * the same view) whether every connected media manager will pick up what Weir writes there. With no manager
 * connected, the Weir-only chain is already a complete, valid setup, so nothing here says a word about that — the
 * separate "No Sonarr, Radarr or Deluno connection covers …" message stays in `LibraryManagerSetup`, above.
 */

import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import type { ProcessingMediaType } from "../../../../lib/processing/libraries-api";
import type {
  FolderChainLine,
  LibraryFolderChainDownloadClient,
} from "../../../../lib/processing/library-folder-chain-api";
import type { ProcessingManagerSetupItem } from "../../../../lib/processing/library-managers-api";
import { useLibraryFolderChainQuery } from "../../../../lib/processing/libraries-queries";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";

/** The folders as the user types them settle for a moment before each check, same as the media-manager check above. */
const SETTLE_MS = 700;

function ChainLines({ lines }: { lines: FolderChainLine[] }) {
  return (
    <ul className="space-y-1.5 text-sm leading-5">
      {lines.map((line, index) => (
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

function ReadinessBadge({ ready }: { ready: boolean }) {
  return (
    <span
      className={`ml-2 text-xs ${ready ? "mm-status-text--healthy" : "mm-status-text--warning"}`}
    >
      {ready ? "Ready" : "Needs attention"}
    </span>
  );
}

function ManagerSection({ item }: { item: ProcessingManagerSetupItem }) {
  return (
    <section aria-label={item.label} className="space-y-3">
      <p className="text-sm font-medium text-mm-text1">
        {item.label}
        <ReadinessBadge ready={item.ready} />
      </p>
      <ChainLines lines={item.lines} />
    </section>
  );
}

function DownloadClientSection({
  item,
}: {
  item: LibraryFolderChainDownloadClient;
}) {
  return (
    <section aria-label={item.label} className="space-y-3">
      <p className="text-sm font-medium text-mm-text1">
        {item.label}
        <ReadinessBadge ready={item.ready} />
      </p>
      <ChainLines lines={item.lines} />
    </section>
  );
}

export function LibraryFolderChain({
  libraryId,
  watchedFolder,
  workFolder,
  outputFolder,
  mediaType,
}: {
  /** The saved library's id; undefined while a new library has not been saved yet. */
  libraryId: number | undefined;
  watchedFolder: string;
  workFolder: string;
  outputFolder: string;
  mediaType: ProcessingMediaType;
}) {
  const settled = useDebouncedValue(
    {
      watched: watchedFolder.trim(),
      work: workFolder.trim(),
      output: outputFolder.trim(),
      mediaType,
    },
    SETTLE_MS,
  );
  const chain = useLibraryFolderChainQuery(
    libraryId,
    settled.watched,
    settled.work,
    settled.output,
    settled.mediaType,
  );

  if (libraryId === undefined) {
    return (
      <QuietFieldGroup
        title="Folder chain"
        detail="Whether Weir's own folders line up, end to end, and whether each connected media manager will pick up what Weir writes."
      >
        <p className="mm-quiet-note">
          Save this library first to check its folder chain.
        </p>
      </QuietFieldGroup>
    );
  }

  return (
    <QuietFieldGroup
      title="Folder chain"
      detail="Whether Weir's own folders line up, end to end, and whether each connected media manager will pick up what Weir writes."
      aside={
        <button
          type="button"
          className="mm-quiet-link"
          onClick={() => void chain.refetch()}
          disabled={chain.isFetching}
        >
          {chain.isFetching ? "Checking…" : "Check again"}
        </button>
      }
    >
      <div data-testid="library-folder-chain" className="space-y-6">
        {chain.isLoading ? (
          <p className="mm-quiet-note">Checking this library&apos;s folders…</p>
        ) : chain.isError ? (
          <p className="mm-quiet-note mm-status-text--warning" role="alert">
            Weir could not check this library&apos;s folder chain just now. Try
            Check again in a moment.
          </p>
        ) : chain.data ? (
          <>
            <section aria-label="Weir's own folders" className="space-y-3">
              <p className="text-sm font-medium text-mm-text1">
                Weir&apos;s own folders
                <ReadinessBadge ready={chain.data.local.ready} />
              </p>
              <ChainLines lines={chain.data.local.lines} />
            </section>
            {chain.data.managers.map((item) => (
              <ManagerSection key={item.connection_id} item={item} />
            ))}
            {chain.data.download_clients.map((item) => (
              <DownloadClientSection key={item.connection_id} item={item} />
            ))}
          </>
        ) : null}
      </div>
    </QuietFieldGroup>
  );
}
