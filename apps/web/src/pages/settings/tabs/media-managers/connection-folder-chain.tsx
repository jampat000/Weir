/**
 * For one media manager connection: whether each workflow it feeds is fully chained end to end (Weir's own folders
 * plus this connection's own setup), compact enough to sit under `FedWorkflows` without pushing the card around.
 * The lines behind each answer are shown once, in the workflow editor's Folder chain, which each row opens.
 */

import { Link } from "react-router-dom";

import {
  READINESS_CLASSES,
  READINESS_LABELS,
  readinessOf,
  type LibraryFolderChain,
} from "../../../../lib/processing/library-folder-chain-api";
import {
  useConnectionFolderChainQuery,
  useProcessingLibrariesQuery,
} from "../../../../lib/processing/libraries-queries";

function libraryName(
  libraries: { id: number; name: string }[] | undefined,
  libraryId: number,
): string {
  return (
    libraries?.find((library) => library.id === libraryId)?.name ??
    `Workflow ${libraryId}`
  );
}

function chainLines(chain: LibraryFolderChain) {
  return [
    ...chain.local.lines,
    ...chain.managers.flatMap((manager) => manager.lines),
    ...chain.download_clients.flatMap((client) => client.lines),
  ];
}

export function ConnectionFolderChain({
  connectionId,
}: {
  connectionId: number;
}) {
  const chain = useConnectionFolderChainQuery(connectionId);
  const libraries = useProcessingLibrariesQuery();

  if (chain.isLoading) {
    return (
      <p className="mt-2 text-xs text-mm-text2">
        Checking its workflows&apos; folder chains…
      </p>
    );
  }

  if (chain.isError) {
    // Quiet failure, like FedWorkflows above it: this is an auxiliary, always-on background check inside a card
    // full of other alerts, not a user-initiated action, so it does not compete for the page's one role="alert".
    return (
      <p className="mm-status-text--warning mt-2 text-xs">
        Weir could not check its workflows&apos; folder chains just now.
      </p>
    );
  }

  const entries = chain.data ?? [];
  if (entries.length === 0) {
    return null;
  }

  return (
    <div className="mt-2 space-y-1" data-testid="media-manager-folder-chain">
      {entries.map((entry) => {
        const readiness = readinessOf(entry.ready, chainLines(entry));
        return (
          <p key={entry.library_id} className="text-xs text-mm-text2">
            {libraryName(libraries.data, entry.library_id)}{" "}
            <span className={READINESS_CLASSES[readiness]}>
              {READINESS_LABELS[readiness]}
            </span>{" "}
            <Link
              className="mm-quiet-link"
              to={`/settings?tab=libraries&edit=${entry.library_id}`}
            >
              See its folder chain
            </Link>
          </p>
        );
      })}
    </div>
  );
}
