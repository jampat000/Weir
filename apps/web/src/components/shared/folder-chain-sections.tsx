import {
  READINESS_CLASSES,
  READINESS_LABELS,
  readinessOf,
  type FolderChainLine,
  type LibraryFolderChain,
} from "../../lib/processing/library-folder-chain-api";
import { SetupCheckLines } from "./setup-check-lines";

function ReadinessBadge({
  ready,
  lines,
}: {
  ready: boolean;
  lines: FolderChainLine[];
}) {
  const readiness = readinessOf(ready, lines);
  return (
    <span className={`ml-2 text-xs ${READINESS_CLASSES[readiness]}`}>
      {READINESS_LABELS[readiness]}
    </span>
  );
}

function ChainSection({
  label,
  ready,
  lines,
}: {
  label: string;
  ready: boolean;
  lines: FolderChainLine[];
}) {
  return (
    <section aria-label={label} className="space-y-3">
      <p className="text-sm font-medium text-mm-text1">
        {label}
        <ReadinessBadge ready={ready} lines={lines} />
      </p>
      <SetupCheckLines label={label} lines={lines} />
    </section>
  );
}

/** A folder chain's lines, grouped: Weir's own folders, then each media manager, then each download client. */
export function FolderChainSections({ chain }: { chain: LibraryFolderChain }) {
  return (
    <>
      <ChainSection
        label="Weir's own folders"
        ready={chain.local.ready}
        lines={chain.local.lines}
      />
      {chain.managers.map((item) => (
        <ChainSection
          key={`manager-${item.connection_id}`}
          label={item.label}
          ready={item.ready}
          lines={item.lines}
        />
      ))}
      {chain.download_clients.map((item) => (
        <ChainSection
          key={`client-${item.connection_id}`}
          label={item.label}
          ready={item.ready}
          lines={item.lines}
        />
      ))}
    </>
  );
}
