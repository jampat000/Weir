/**
 * The "folder chain": whether Weir's own watched, work and output folders line up end to end, plus (folded into
 * the same view) whether every connected media manager will pick up what Weir writes there. With no manager
 * connected, the Weir-only chain is already a complete, valid setup, so nothing here says a word about that — the
 * separate "No Sonarr, Radarr or Deluno connection covers …" message stays in `LibraryManagerSetup`, above.
 */

import { QuietDisclosure } from "../../../../components/shared/quiet-section";
import { FolderChainSections } from "../../../../components/shared/folder-chain-sections";
import type { ProcessingMediaType } from "../../../../lib/processing/libraries-api";
import { useLibraryFolderChainQuery } from "../../../../lib/processing/libraries-queries";
import { useDebouncedValue } from "../../../../lib/ui/use-debounced-value";
import { FOLDER_CHECK_SETTLE_MS } from "./folder-check-settle";

const CHAIN_DETAIL =
  "Whether Weir's own folders line up end to end, and whether each connected media manager will pick up what Weir writes.";

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
    FOLDER_CHECK_SETTLE_MS,
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
      <QuietDisclosure title="Folder chain" detail={CHAIN_DETAIL}>
        <p className="mm-quiet-note">
          Save this workflow first to check its folder chain.
        </p>
      </QuietDisclosure>
    );
  }

  return (
    <QuietDisclosure title="Folder chain" detail={CHAIN_DETAIL} defaultOpen>
      <div data-testid="library-folder-chain" className="space-y-6">
        {chain.isLoading ? (
          <p className="mm-quiet-note">
            Checking this workflow&apos;s folders…
          </p>
        ) : chain.isError ? (
          <p className="mm-quiet-note mm-status-text--warning" role="alert">
            Weir could not check this workflow&apos;s folder chain just now. Try
            Check again in a moment.
          </p>
        ) : chain.data ? (
          <FolderChainSections chain={chain.data} />
        ) : null}
      </div>
      <button
        type="button"
        className="mm-quiet-link mt-4"
        onClick={() => void chain.refetch()}
        disabled={chain.isFetching}
      >
        {chain.isFetching ? "Checking…" : "Check again"}
      </button>
    </QuietDisclosure>
  );
}
