import { useQueries } from "@tanstack/react-query";

import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import { useDownloadClientConnectionsQuery } from "../../../lib/download-clients/queries";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import { useMediaManagerConnectionsQuery } from "../../../lib/media-managers/queries";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { libraryFolderChainOptions } from "../../../lib/processing/libraries-queries";
import type { LibraryFolderChain } from "../../../lib/processing/library-folder-chain-api";
import { useMediaToolsQuery } from "../../../lib/system/media-tools";
import {
  checkVerdict,
  toolRows,
  whyNotInSync,
  type ToolRow,
  type WorkflowVerdict,
} from "./health-model";

const NO_MANAGERS: MediaManagerConnection[] = [];
const NO_CLIENTS: DownloadClientConnection[] = [];
/** A folder check reads the disk and asks each manager, so it is repeated slowly. */
const FOLDER_CHECK_REFRESH_MS = 120_000;

export type WorkflowHealth = {
  workflow: ProcessingLibrary;
  verdict: WorkflowVerdict;
  /** One plain line on why the workflow is not in sync. Null when it is, or while the check has not answered. */
  why: string | null;
  /** The whole chain, for the views that list every line. */
  chain: LibraryFolderChain | undefined;
  /** When the chain was last read, in ms since the epoch. Null before the first answer. */
  checkedAt: number | null;
  /** Reads this workflow's chain again. */
  recheck: () => Promise<unknown>;
};

export type Health = {
  workflows: WorkflowHealth[];
  /** The media managers in scope, switched on or not. */
  managers: MediaManagerConnection[];
  /** The download clients in scope, switched on or not. */
  downloadClients: DownloadClientConnection[];
  tools: ToolRow[] | null;
  /** Asks every workflow in scope for its folder chain again. */
  recheckFolders: () => Promise<void>;
};

/** A workflow's own media managers, and the download clients its chain names. */
function inScope(
  workflowId: number | null | undefined,
  rows: WorkflowHealth[],
  managers: MediaManagerConnection[],
  downloadClients: DownloadClientConnection[],
) {
  if (workflowId == null) return { managers, downloadClients };
  const only = rows.find((row) => row.workflow.id === workflowId);
  const clientIds = new Set(
    only?.chain?.download_clients.map((client) => client.connection_id),
  );
  const managerIds = new Set(only?.workflow.manager_connection_ids);
  return {
    managers: managers.filter((manager) => managerIds.has(manager.id)),
    downloadClients: downloadClients.filter((client) =>
      clientIds.has(client.id),
    ),
  };
}

/**
 * Each enabled workflow's folder-chain verdict, the connections and the tools: every part loads on its own.
 * Given a workflow, everything narrows to it: its chain, the managers it is linked to and the download
 * clients its chain names. The tools belong to Weir, so they stay.
 */
export function useHealth(
  workflows: readonly ProcessingLibrary[],
  workflowId?: number | null,
): Health {
  const enabled = workflows.filter(
    (workflow) =>
      workflow.enabled && (workflowId == null || workflow.id === workflowId),
  );
  const chains = useQueries({
    queries: enabled.map((workflow) => ({
      ...libraryFolderChainOptions(
        workflow.id,
        workflow.watched_folder.trim(),
        workflow.work_folder.trim(),
        workflow.output_folder.trim(),
        workflow.media_type,
      ),
      refetchInterval: FOLDER_CHECK_REFRESH_MS,
    })),
  });
  const managers = useMediaManagerConnectionsQuery();
  const downloadClients = useDownloadClientConnectionsQuery();
  const tools = useMediaToolsQuery();
  const rows = enabled.map((workflow, index) => {
    const chain = chains[index];
    return {
      workflow,
      verdict: checkVerdict(chain),
      why: chain.data ? whyNotInSync(chain.data) : null,
      chain: chain.data,
      checkedAt: chain.dataUpdatedAt > 0 ? chain.dataUpdatedAt : null,
      recheck: () => chain.refetch(),
    };
  });
  return {
    workflows: rows,
    ...inScope(
      workflowId,
      rows,
      managers.data ?? NO_MANAGERS,
      downloadClients.data ?? NO_CLIENTS,
    ),
    tools: tools.data ? toolRows(tools.data) : null,
    recheckFolders: async () => {
      await Promise.all(chains.map((chain) => chain.refetch()));
    },
  };
}
