import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import type { DownloadClientConnection } from "../../../../lib/download-clients/download-clients-api";
import { useDownloadClientConnectionsQuery } from "../../../../lib/download-clients/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { SettingsLoadError } from "../../settings-load-error";
import { AddDownloadClientForm } from "./add-download-client-form";
import { DownloadClientCard } from "./download-client-card";

/**
 * Settings: bare download clients (SABnzbd, NZBGet, qBittorrent, Deluge, Transmission) Weir can read a
 * watched-folder suggestion from. Composes the list and the add form; the intro paragraph lives in the
 * parent tab, next to the media managers it sits below.
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
          folders from Sonarr, Radarr or Deluno above; add one below only if you
          run a bare download client Weir should read instead.
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
          {justCreated.name} is connected. Weir will offer its folder as a
          suggestion in the library editor — nothing is applied on its own.
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
        <div>
          <button
            type="button"
            data-testid="download-client-add"
            className={mmActionButtonClass({ variant: "primary" })}
            onClick={() => {
              setJustCreated(null);
              setAdding(true);
            }}
          >
            Add a download client
          </button>
        </div>
      )}
    </div>
  );
}
