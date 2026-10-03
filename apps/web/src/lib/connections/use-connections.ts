import { useMemo } from "react";

import type { DownloadClientConnection } from "../download-clients/download-clients-api";
import type { MediaManagerConnection } from "../media-managers/media-managers-api";
import {
  useConnectionActivity,
  type ConnectionLight,
} from "./connection-lights";
import { connectionEntries, type ConnectionEntry } from "./connection-model";

export type Connections = {
  entries: ConnectionEntry[];
  /** The light each row shows now, by `ConnectionEntry.key`. */
  lights: ReadonlyMap<string, ConnectionLight>;
};

/**
 * The media managers and download clients as rows: their state as of the saved lists, brought up to date by the
 * answers pushed since, with the lights the stream is lighting now.
 */
export function useConnections(
  managers: readonly MediaManagerConnection[],
  downloadClients: readonly DownloadClientConnection[],
): Connections {
  const { lights, answers } = useConnectionActivity();
  const entries = useMemo(
    () => connectionEntries(managers, downloadClients, answers),
    [managers, downloadClients, answers],
  );
  return { entries, lights };
}
