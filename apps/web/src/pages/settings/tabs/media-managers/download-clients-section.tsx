import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { PageToolbarAddButton } from "../../../../components/shell/page-toolbar-actions";
import type { DownloadClientConnection } from "../../../../lib/download-clients/download-clients-api";
import { useDownloadClientConnectionsQuery } from "../../../../lib/download-clients/queries";
import { connectionTitle } from "../../../../lib/ui/connection-title";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { SettingsLoadError } from "../../settings-load-error";
import { AddDownloadClientForm } from "./add-download-client-form";
import { DownloadClientCard } from "./download-client-card";

/**
 * Bare download clients (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission) Weir can read a
 * watched-folder suggestion from. Composes the list and the add form; the intro paragraph lives in the
 * tab that holds it.
 */
export function DownloadClientsSection() {
  const connections = useDownloadClientConnectionsQuery();
  const fmt = useAppDateFormatter();
  const [adding, setAdding] = useState(false);
  const [justCreated, setJustCreated] =
    useState<DownloadClientConnection | null>(null);

  if (connections.isPending)
    return <PageLoading label="Loading download clients" />;
  if (connections.isError) return <SettingsLoadError what="download clients" />;

  return (
    <div
      className="mm-quiet-stack"
      data-testid="suite-settings-download-clients"
    >
      {connections.data.length === 0 && !adding ? (
        <p className="mm-quiet-note">
          No download client is connected. Weir can still suggest watched
          folders from Sonarr, Radarr or Deluno under Media managers; add one
          only if you run a bare download client Weir should read instead.
        </p>
      ) : null}

      {connections.data.map((connection) => (
        <DownloadClientCard
          key={connection.id}
          connection={connection}
          fmt={fmt}
        />
      ))}

      {justCreated ? (
        <p className="mm-quiet-note" data-testid="download-client-created-note">
          {connectionTitle(justCreated)} is connected. Weir will offer its
          folder as a suggestion in the workflow editor — nothing is applied on
          its own.
        </p>
      ) : null}

      {adding ? (
        <AddDownloadClientForm
          onCancel={() => setAdding(false)}
          onCreated={(connection) => {
            setAdding(false);
            setJustCreated(connection);
          }}
        />
      ) : (
        <PageToolbarAddButton
          label="Add download client"
          dataTestId="download-client-add"
          onClick={() => {
            setJustCreated(null);
            setAdding(true);
          }}
        />
      )}
    </div>
  );
}
