/** What the full Health view says about connections and disk space, beyond what the compact panel shows. */
import {
  DOWNLOAD_CLIENT_KIND_LABELS,
  type DownloadClientConnection,
} from "../../../lib/download-clients/download-clients-api";
import { formatBytes } from "../../../lib/format/bytes";
import {
  MEDIA_MANAGER_KIND_LABELS,
  type MediaManagerConnection,
} from "../../../lib/media-managers/media-managers-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { connectionTitle } from "../../../lib/ui/connection-title";
import type { MmStatusTone } from "../../../lib/ui/mm-status-tone";
import { answering } from "./health-model";

const BYTES_PER_MB = 1024 * 1024;
export const NO_FREE_SPACE_LIMIT = "no limit";

export type ConnectionDetail = {
  key: string;
  name: string;
  role: "Media manager" | "Download client";
  /** Radarr, qBittorrent… */
  kind: string;
  /** Where Weir reaches it. Empty for a manager that only sends to Weir. */
  address: string;
  state: string;
  tone: MmStatusTone;
  /** What the last test said, when it said anything. */
  detail: string;
};

const SWITCHED_OFF = { state: "switched off", tone: "neutral" } as const;

type Described = {
  enabled: boolean;
  base_url: string;
  last_test_ok?: boolean | null;
  last_test_at?: string | null;
  last_test_detail?: string | null;
};

function describe(connection: Described, now: number) {
  return {
    address: connection.base_url,
    ...(connection.enabled ? answering(connection, now) : SWITCHED_OFF),
    detail: connection.last_test_detail?.trim() ?? "",
  };
}

/** Every media manager and download client, switched on or not, with its last answer. */
export function connectionDetails(
  managers: readonly MediaManagerConnection[],
  downloadClients: readonly DownloadClientConnection[],
  now: number,
): ConnectionDetail[] {
  return [
    ...managers.map((manager) => ({
      key: `manager-${manager.id}`,
      name: connectionTitle(manager),
      role: "Media manager" as const,
      kind: MEDIA_MANAGER_KIND_LABELS[manager.kind],
      ...describe(manager, now),
    })),
    ...downloadClients.map((client) => ({
      key: `client-${client.id}`,
      name: connectionTitle(client),
      role: "Download client" as const,
      kind: DOWNLOAD_CLIENT_KIND_LABELS[client.kind],
      ...describe(client, now),
    })),
  ];
}

export type DiskRow = {
  key: number;
  workflow: string;
  outputFolder: string;
  /** What Weir keeps free there: "10 GB", or "no limit". */
  keepFree: string;
};

/** Each workflow's output drive and how much room Weir keeps free on it. */
export function diskRows(workflows: readonly ProcessingLibrary[]): DiskRow[] {
  return workflows.map((workflow) => ({
    key: workflow.id,
    workflow: workflow.name,
    outputFolder: workflow.output_folder,
    keepFree:
      workflow.minimum_free_disk_space_mb > 0
        ? formatBytes(workflow.minimum_free_disk_space_mb * BYTES_PER_MB)
        : NO_FREE_SPACE_LIMIT,
  }));
}
