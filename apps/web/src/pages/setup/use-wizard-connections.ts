import type { DownloadClientConnection } from "../../lib/download-clients/download-clients-api";
import { useDownloadClientConnectionsQuery } from "../../lib/download-clients/queries";
import type { MediaManagerConnection } from "../../lib/media-managers/media-managers-api";
import { useMediaManagerConnectionsQuery } from "../../lib/media-managers/queries";
import { MANAGER_KINDS_BY_SOURCE, type ConnectedSource } from "./wizard-source";

export type WizardConnections = {
  /** The media managers of the chosen source; none for a download client. */
  managers: MediaManagerConnection[];
  /** The download clients, when that is the chosen source; none otherwise. */
  clients: DownloadClientConnection[];
  loading: boolean;
  failed: boolean;
  /** One key per enabled connection whose last test succeeded: what the suggested libraries are built from. */
  answering: string[];
};

function answered(connection: {
  enabled: boolean;
  last_test_ok?: boolean | null;
  last_test_at?: string | null;
}): boolean {
  return connection.enabled && connection.last_test_at
    ? connection.last_test_ok === true
    : false;
}

/** The connections that belong to the chosen source, and which of them are answering. */
export function useWizardConnections(
  source: ConnectedSource | null,
): WizardConnections {
  const managersQuery = useMediaManagerConnectionsQuery(
    source === "deluno" || source === "arr",
  );
  const clientsQuery = useDownloadClientConnectionsQuery(source === "client");

  const kinds =
    source === "deluno" || source === "arr"
      ? MANAGER_KINDS_BY_SOURCE[source]
      : [];
  const managers = (managersQuery.data ?? []).filter((connection) =>
    kinds.includes(connection.kind),
  );
  const clients = source === "client" ? (clientsQuery.data ?? []) : [];

  return {
    managers,
    clients,
    loading: managersQuery.isLoading || clientsQuery.isLoading,
    failed: managersQuery.isError || clientsQuery.isError,
    answering: [
      ...managers.filter(answered).map((c) => `manager:${c.id}`),
      ...clients.filter(answered).map((c) => `client:${c.id}`),
    ],
  };
}
