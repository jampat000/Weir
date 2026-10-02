import { useRef, useState } from "react";

import { Panel } from "../../../../components/panels/panel";
import { EmptyState } from "../../../../components/shared/empty-state";
import { PageLoading } from "../../../../components/shared/page-loading";
import { SidePanel } from "../../../../components/shared/side-panel";
import { PageToolbarAddButton } from "../../../../components/shell/page-toolbar-actions";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useCreateNotificationChannelMutation,
  useNotificationChannelsQuery,
} from "../../../../lib/settings/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { orderedEvents } from "./alert-events";
import { ChannelForm } from "./channel-form";
import { ChannelTable } from "./channel-table";
import {
  RemoveChannelDialog,
  useChannelRemoval,
} from "./remove-channel-dialog";

function NewChannelPanel({
  supportedEvents,
  onClose,
}: {
  supportedEvents: string[];
  onClose: () => void;
}) {
  const createMutation = useCreateNotificationChannelMutation();
  const labelRef = useRef<HTMLInputElement>(null);
  return (
    <SidePanel
      open
      title="New alert"
      eyebrow="Setup · Connections"
      initialFocus={labelRef}
      onClose={onClose}
      dataTestId="alert-add-panel"
    >
      <ChannelForm
        supportedEvents={supportedEvents}
        labelRef={labelRef}
        onSave={(data) => createMutation.mutate(data, { onSuccess: onClose })}
        onCancel={onClose}
        saving={createMutation.isPending}
        saveError={
          createMutation.isError
            ? errorMessage(createMutation.error, "Could not create alert.")
            : null
        }
      />
    </SidePanel>
  );
}

export function AlertsTab() {
  const channelsQ = useNotificationChannelsQuery();
  const removal = useChannelRemoval();
  const [adding, setAdding] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);

  if (channelsQ.isPending) return <PageLoading label="Loading alerts" />;
  if (channelsQ.isError) return <SettingsLoadError what="alerts" />;

  const supportedEvents = orderedEvents(channelsQ.data.supported_events);
  const channels = channelsQ.data.items;

  return (
    <div data-testid="suite-settings-notifications" className="mm-quiet-stack">
      {channels.length > 0 ? (
        <Panel
          title="Where alerts go"
          count="Tick what each one hears about. Use Send test before relying on a new one."
          aside={<SaveModelNote model="instant" />}
          padded
        >
          <ChannelTable
            channels={channels}
            supportedEvents={supportedEvents}
            editingId={editingId}
            deletingId={removal.deletingId}
            onEdit={setEditingId}
            onRemove={removal.ask}
          />
          <p className="mm-quiet-note mt-4">
            A failure Weir will retry is sent only after its last try. When an
            alert does not answer, Weir notes it in System › Logs.
          </p>
        </Panel>
      ) : (
        <EmptyState
          title="No alerts yet"
          testId="alerts-empty"
          action={
            <button
              type="button"
              className={mmActionButtonClass({ variant: "secondary" })}
              onClick={() => setAdding(true)}
            >
              Add your first alert
            </button>
          }
        >
          An alert is a message Weir posts to Discord, or to any address that
          takes a webhook, when a file finishes or fails for good, or one of
          Weir&rsquo;s own jobs does. Nothing is sent until you add one.
        </EmptyState>
      )}

      <PageToolbarAddButton
        label="Add alert"
        disabled={editingId !== null}
        onClick={() => setAdding(true)}
      />
      {adding ? (
        <NewChannelPanel
          supportedEvents={supportedEvents}
          onClose={() => setAdding(false)}
        />
      ) : null}

      <RemoveChannelDialog removal={removal} />
    </div>
  );
}
