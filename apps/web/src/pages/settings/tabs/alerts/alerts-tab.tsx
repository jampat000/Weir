import { useState } from "react";

import { PageLoading } from "../../../../components/shared/page-loading";
import {
  QuietFieldGroup,
  QuietSection,
} from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useCreateNotificationChannelMutation,
  useNotificationChannelsQuery,
} from "../../../../lib/settings/queries";
import { SaveModelNote } from "../../save-model-note";
import { SettingsLoadError } from "../../settings-load-error";
import { orderedEvents } from "./alert-events";
import { ChannelForm } from "./channel-form";
import { ChannelTable } from "./channel-table";
import {
  RemoveChannelDialog,
  useChannelRemoval,
} from "./remove-channel-dialog";

function AlertIntro() {
  return (
    <>
      <p className="mm-quiet-note">
        An alert is a message Weir posts to Discord, or to any address that
        takes a webhook, when something happens you would want to know about
        without opening Weir. Nothing is sent until you add a channel.
      </p>
      <ol className="mm-alert-steps" aria-label="How an alert is sent">
        <li>
          <b>Something happens.</b> A file finishes or fails for good, or one of
          Weir&rsquo;s own jobs fails for good. A failure Weir will retry is not
          sent until the last try.
        </li>
        <li>
          <b>Weir checks each channel.</b> Only channels ticked for that event,
          and switched on, get it.
        </li>
        <li>
          <b>It posts the message.</b> If a channel does not answer, Weir notes
          it in System › Logs, under Server log.
        </li>
      </ol>
    </>
  );
}

function NewChannel({
  supportedEvents,
  onDone,
}: {
  supportedEvents: string[];
  onDone: () => void;
}) {
  const createMutation = useCreateNotificationChannelMutation();
  return (
    <QuietFieldGroup title="New alert" className="mt-6">
      <ChannelForm
        supportedEvents={supportedEvents}
        onSave={(data) => createMutation.mutate(data, { onSuccess: onDone })}
        onCancel={onDone}
        saving={createMutation.isPending}
        saveError={
          createMutation.isError
            ? errorMessage(createMutation.error, "Could not create alert.")
            : null
        }
      />
    </QuietFieldGroup>
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
      <AlertIntro />
      <SaveModelNote model="instant" />

      <QuietSection
        level={3}
        headingId="suite-settings-notifications-heading"
        heading="Channels"
        aside={
          !adding && editingId === null ? (
            <button
              type="button"
              className="mm-quiet-link"
              onClick={() => setAdding(true)}
            >
              Add an alert →
            </button>
          ) : null
        }
      >
        <p className="mm-quiet-note">
          Tick what each channel should hear about; a change saves straight
          away. Use &ldquo;Send test&rdquo; before relying on a new alert.
        </p>

        {channels.length > 0 ? (
          <ChannelTable
            channels={channels}
            supportedEvents={supportedEvents}
            editingId={editingId}
            deletingId={removal.deletingId}
            onEdit={setEditingId}
            onRemove={removal.ask}
          />
        ) : !adding ? (
          <p className="mm-quiet-note mt-4">No alerts configured yet.</p>
        ) : null}

        {adding ? (
          <NewChannel
            supportedEvents={supportedEvents}
            onDone={() => setAdding(false)}
          />
        ) : null}
      </QuietSection>

      <RemoveChannelDialog removal={removal} />
    </div>
  );
}
