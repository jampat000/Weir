import { useState } from "react";

import { useMediaManagerConnectionsQuery } from "../../../../lib/media-managers/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { AddConnectionForm } from "./add-connection-form";
import { ConnectionCard } from "./connection-card";

/** Settings: the apps that send files to Weir. */
export function MediaManagersTab() {
  const connections = useMediaManagerConnectionsQuery();
  const fmt = useAppDateFormatter();
  const [adding, setAdding] = useState(false);

  return (
    <div className="mm-quiet-stack" data-testid="suite-settings-media-managers">
      <p className="mm-quiet-note">
        The apps that send files to Weir. Weir asks each one when a download is
        really finished, hands cleaned files back to it, and can ask it for a
        different release when one is bad. Libraries imported from an app are
        linked to it; link a library you made yourself in its editor under
        Libraries. Weir checks every app each minute and says here when one
        stops answering.
      </p>

      {connections.isLoading ? <p className="mm-quiet-note">Loading…</p> : null}

      {connections.data?.length === 0 && !adding ? (
        <p className="mm-quiet-note">
          Nothing is connected yet, so no files are reaching Weir. Add an app
          below to get started.
        </p>
      ) : null}

      {connections.data?.map((connection) => (
        <ConnectionCard key={connection.id} connection={connection} fmt={fmt} />
      ))}

      {adding ? (
        <AddConnectionForm onCancel={() => setAdding(false)} />
      ) : (
        <div>
          <button
            type="button"
            data-testid="media-manager-add"
            className={mmActionButtonClass({ variant: "primary" })}
            onClick={() => setAdding(true)}
          >
            Add an app
          </button>
        </div>
      )}
    </div>
  );
}
