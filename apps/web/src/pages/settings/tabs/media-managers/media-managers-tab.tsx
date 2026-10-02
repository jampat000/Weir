import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import { PageToolbarAddButton } from "../../../../components/shell/page-toolbar-actions";
import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { useMediaManagerConnectionsQuery } from "../../../../lib/media-managers/queries";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { AddConnectionForm } from "./add-connection-form";
import { ConnectionCard } from "./connection-card";
import { NewConnectionSecretPrompt } from "./new-connection-secret-prompt";
import { WeirOnlyWorkflows } from "./weir-only-workflows";

/** Setup › Connections › Media managers: the media managers that send files to Weir. */
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
        it for a different release when one is bad. Workflows imported from a
        media manager are linked to it; link a workflow you made yourself in its
        editor under Workflows. Weir checks every media manager each minute and
        says here when one stops answering.
      </p>

      {connections.data.length === 0 && !adding ? (
        <p className="mm-quiet-note">
          Nothing is connected yet, so no files are reaching Weir. Add a media
          manager to get started.
        </p>
      ) : null}

      {connections.data.map((connection) => (
        <ConnectionCard key={connection.id} connection={connection} fmt={fmt} />
      ))}

      <WeirOnlyWorkflows />

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
        <PageToolbarAddButton
          label="Add media manager"
          dataTestId="media-manager-add"
          onClick={() => {
            setJustCreated(null);
            setAdding(true);
          }}
        />
      )}
    </div>
  );
}
