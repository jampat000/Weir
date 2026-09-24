import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { useMediaManagerConnectionsQuery } from "../../../../lib/media-managers/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { AddConnectionForm } from "./add-connection-form";
import { ConnectionCard } from "./connection-card";
import { NewConnectionSecretPrompt } from "./new-connection-secret-prompt";

/** Settings: the media managers that send files to Weir. */
export function MediaManagersTab() {
  const connections = useMediaManagerConnectionsQuery();
  const fmt = useAppDateFormatter();
  const [adding, setAdding] = useState(false);
  const [justCreated, setJustCreated] = useState<MediaManagerConnection | null>(
    null,
  );

  if (connections.isPending)
    return <PageLoading label="Loading media managers" />;
  if (connections.isError) return <SettingsLoadError what="media managers" />;

  return (
    <div className="mm-quiet-stack" data-testid="suite-settings-media-managers">
      <SaveModelNote model="instant" />
      <p className="mm-quiet-note">
        The media managers that send files to Weir. Weir asks each one when a
        download is really finished, hands cleaned files back to it, and can ask
        it for a different release when one is bad. Libraries imported from a
        media manager are linked to it; link a library you made yourself in its
        editor under Libraries. Weir checks every media manager each minute and
        says here when one stops answering.
      </p>

      {connections.data.length === 0 && !adding ? (
        <p className="mm-quiet-note">
          Nothing is connected yet, so no files are reaching Weir. Add a media
          manager below to get started.
        </p>
      ) : null}

      {connections.data.map((connection) => (
        <ConnectionCard key={connection.id} connection={connection} fmt={fmt} />
      ))}

      {justCreated ? (
        <NewConnectionSecretPrompt
          connection={justCreated}
          onDismiss={() => setJustCreated(null)}
        />
      ) : null}

      {adding ? (
        <AddConnectionForm
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
            data-testid="media-manager-add"
            className={mmActionButtonClass({ variant: "primary" })}
            onClick={() => {
              setJustCreated(null);
              setAdding(true);
            }}
          >
            Add a media manager
          </button>
        </div>
      )}
    </div>
  );
}
