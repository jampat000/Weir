/**
 * #768, for one media manager connection: which of its linked libraries are fully chained end to end (Weir's own
 * folders plus this connection's own setup), compact enough to sit under `LinkedLibraries` without pushing the card
 * around. Each library's own detail is the same lines `LibraryFolderChain` shows in the library editor.
 */

import type { LibraryFolderChain } from "../../../../lib/processing/library-folder-chain-api";
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
    `Library ${libraryId}`
  );
}

function ChainDetail({ chain }: { chain: LibraryFolderChain }) {
  const lines = [
    ...chain.local.lines,
    ...chain.managers.flatMap((manager) => manager.lines),
    ...chain.download_clients.flatMap((client) => client.lines),
  ];
  return (
    <ul className="mt-1 space-y-1 pl-4">
      {lines.map((line, index) => (
        <li
          key={index}
          className={
            line.state === "problem"
              ? "mm-status-text--warning"
              : "text-mm-text2"
          }
        >
          {line.text}
        </li>
      ))}
    </ul>
  );
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
        Checking its libraries&apos; folder chains…
      </p>
    );
  }

  if (chain.isError) {
    // Quiet failure, like LinkedLibraries above it: this is an auxiliary, always-on background check inside a card
    // full of other alerts, not a user-initiated action, so it does not compete for the page's one role="alert".
    return (
      <p className="mm-status-text--warning mt-2 text-xs">
        Weir could not check its libraries&apos; folder chains just now.
      </p>
    );
  }

  const entries = chain.data ?? [];
  if (entries.length === 0) {
    return null;
  }

  return (
    <div className="mt-2 space-y-1" data-testid="media-manager-folder-chain">
      {entries.map((entry) => (
        <details key={entry.library_id} className="text-xs text-mm-text2">
          <summary className="cursor-pointer">
            {libraryName(libraries.data, entry.library_id)}{" "}
            <span
              className={
                entry.ready
                  ? "mm-status-text--healthy"
                  : "mm-status-text--warning"
              }
            >
              {entry.ready ? "Ready" : "Needs attention"}
            </span>
          </summary>
          <ChainDetail chain={entry} />
        </details>
      ))}
    </div>
  );
}
