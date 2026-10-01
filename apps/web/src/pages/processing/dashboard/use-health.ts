import { useQueries } from "@tanstack/react-query";

import { useDownloadClientConnectionsQuery } from "../../../lib/download-clients/queries";
import { useMediaManagerConnectionsQuery } from "../../../lib/media-managers/queries";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { libraryFolderChainOptions } from "../../../lib/processing/libraries-queries";
import { useMediaToolsQuery } from "../../../lib/system/media-tools";
import {
  checkVerdict,
  connectionPills,
  toolRows,
  type ConnectionPill,
  type ToolRow,
  type WorkflowVerdict,
} from "./health-model";

const NO_MANAGERS: MediaManagerConnection[] = [];
/** A folder check reads the disk and asks each manager, so it is repeated slowly. */
const FOLDER_CHECK_REFRESH_MS = 120_000;

export type WorkflowHealth = {
  workflow: ProcessingLibrary;
  verdict: WorkflowVerdict;
};

export type Health = {
  workflows: WorkflowHealth[];
  /** The media managers, for the words that say which one a workflow is linked to. */
  managers: MediaManagerConnection[];
  connections: ConnectionPill[];
  tools: ToolRow[] | null;
};

/** Each enabled workflow's folder-chain verdict, the connections and the tools: every part loads on its own. */
export function useHealth(workflows: readonly ProcessingLibrary[]): Health {
  const enabled = workflows.filter((workflow) => workflow.enabled);
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
  return {
    workflows: enabled.map((workflow, index) => {
      const chain = chains[index];
      return { workflow, verdict: checkVerdict(chain) };
    }),
    managers: managers.data ?? NO_MANAGERS,
    connections: connectionPills(
      managers.data ?? [],
      downloadClients.data ?? [],
    ),
    tools: tools.data ? toolRows(tools.data) : null,
  };
}
