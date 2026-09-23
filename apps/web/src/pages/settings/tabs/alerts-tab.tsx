import { useState } from "react";
import type { NotificationChannelOut } from "../../../lib/settings/types";
import {
  useCreateNotificationChannelMutation,
  useDeleteNotificationChannelMutation,
  useNotificationChannelsQuery,
  useTestNotificationChannelMutation,
  useUpdateNotificationChannelMutation,
} from "../../../lib/settings/queries";
import { ConfirmDialog } from "../../../components/ui/confirm-dialog";
import { mmActionButtonClass } from "../../../lib/ui/mm-control-roles";
import {
  QuietFieldGroup,
  QuietSection,
} from "../../../components/shared/quiet-section";
import { Field } from "../../../components/shared/field";
import { errorMessage } from "../../../lib/api/error-message";

/**
 * What each event means, in the order the columns read. "Anything" covers files and Weir's own jobs alike: a file's
 * pass is a job, and Weir's own jobs (backups, cleanup, scans) only ever alert when they fail for good.
 */
const EVENT_LABELS: Record<string, string> = {
  processing_job_completed: "A file finished",
  processing_job_failed: "A file failed for good",
  job_failed: "Anything failed for good",
  job_completed: "Anything finished",
};

const EVENT_ORDER = Object.keys(EVENT_LABELS);

type NotificationFormData = {
  label: string;
  provider: string;
  url: string;
  events: string[];
  enabled: boolean;
};

type ChannelFormProps = {
  initial?: Partial<NotificationFormData>;
  supportedEvents: string[];
  onSave: (data: NotificationFormData) => Promise<void>;
  onCancel: () => void;
  saving: boolean;
  saveError: string | null;
};

function ChannelForm({
  initial,
  supportedEvents,
  onSave,
  onCancel,
  saving,
  saveError,
}: ChannelFormProps) {
  const [label, setLabel] = useState(initial?.label ?? "");
  const [provider, setProvider] = useState<string>(
    initial?.provider ?? "webhook",
  );
  const [url, setUrl] = useState(initial?.url ?? "");
  const [events, setEvents] = useState<string[]>(
    initial?.events ?? ["job_failed"],
  );
  const [enabled, setEnabled] = useState(initial?.enabled ?? true);

  const toggleEvent = (event: string) => {
    setEvents((prev) =>
      prev.includes(event) ? prev.filter((e) => e !== event) : [...prev, event],
    );
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    await onSave({ label, provider, url, events, enabled });
  };

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="space-y-4">
      <div className="mm-field-row">
        <Field label="Label" width="medium">
          <input
            type="text"
            className="mm-input"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            placeholder="e.g. Discord alerts"
            required
            maxLength={255}
            disabled={saving}
          />
        </Field>
        <Field label="Provider" width="medium">
          <select
            className="mm-input"
            value={provider}
            onChange={(e) => setProvider(e.target.value)}
            disabled={saving}
          >
            <option value="webhook">Generic webhook (JSON POST)</option>
            <option value="discord">Discord webhook</option>
          </select>
        </Field>
      </div>

      <Field label="Webhook URL" width="wide">
        <input
          type="url"
          className="mm-input"
          value={url}
          onChange={(e) => setUrl(e.target.value)}
          placeholder="https://..."
          required
          disabled={saving}
        />
      </Field>

      <fieldset>
        <legend className="mm-field__label mb-2">Trigger events</legend>
        <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
          {supportedEvents.map((event) => (
            <label
              key={event}
              className="flex cursor-pointer items-center gap-2 text-sm text-[var(--mm-text2)]"
            >
              <input
                type="checkbox"
                className="h-4 w-4 shrink-0 accent-[var(--mm-accent)]"
                checked={events.includes(event)}
                onChange={() => toggleEvent(event)}
                disabled={saving}
              />
              {EVENT_LABELS[event] ?? event}
            </label>
          ))}
        </div>
      </fieldset>

      <label className="flex cursor-pointer items-center gap-2 text-sm text-[var(--mm-text2)]">
        <input
          type="checkbox"
          className="h-4 w-4 shrink-0 accent-[var(--mm-accent)]"
          checked={enabled}
          onChange={(e) => setEnabled(e.target.checked)}
          disabled={saving}
        />
        Enabled
      </label>

      {saveError ? (
        <p className="mm-status-text--failed text-sm" role="alert">
          {saveError}
        </p>
      ) : null}

      <div className="flex gap-2">
        <button
          type="submit"
          className={mmActionButtonClass({ variant: "primary" })}
          disabled={
            saving || !label.trim() || !url.trim() || events.length === 0
          }
        >
          {saving ? "Saving..." : "Save channel"}
        </button>
        <button
          type="button"
          className={mmActionButtonClass({ variant: "tertiary" })}
          disabled={saving}
          onClick={onCancel}
        >
          Cancel
        </button>
      </div>
    </form>
  );
}

type ChannelRowProps = {
  channel: NotificationChannelOut;
  events: string[];
  onToggleEvent: (event: string) => void;
  toggling: boolean;
  onEdit: () => void;
  onDelete: () => void;
  onTest: () => void;
  testing: boolean;
  testResult: { ok: boolean; error: string | null } | null;
  deleting: boolean;
};

/** One channel as a table row. The hairline under it is what says a Remove button
 *  belongs to this channel and not its neighbour. */
function ChannelRow({
  channel,
  events,
  onToggleEvent,
  toggling,
  onEdit,
  onDelete,
  onTest,
  testing,
  testResult,
  deleting,
}: ChannelRowProps) {
  return (
    <tr>
      <th scope="row" className="mm-quiet-table__name">
        <span>{channel.label}</span>
        <span className="mm-quiet-badge">{channel.provider}</span>
        {!channel.enabled ? (
          <span className="mm-quiet-badge mm-quiet-badge--off">Disabled</span>
        ) : null}
        <span className="mm-quiet-table__sub font-mono">{channel.url}</span>
      </th>
      {events.map((event) => (
        <td
          key={event}
          data-label={EVENT_LABELS[event] ?? event}
          className="mm-alerts-cell"
        >
          <input
            type="checkbox"
            className="h-4 w-4 accent-[var(--mm-accent)]"
            aria-label={`${channel.label}: ${EVENT_LABELS[event] ?? event}`}
            checked={channel.events.includes(event)}
            disabled={toggling}
            onChange={() => onToggleEvent(event)}
          />
        </td>
      ))}
      <td data-label="">
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            disabled={testing || deleting}
            onClick={onTest}
          >
            {testing ? "Testing..." : "Send test"}
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={deleting}
            onClick={onEdit}
          >
            Edit
          </button>
          <button
            type="button"
            className={mmActionButtonClass({ variant: "tertiary" })}
            disabled={deleting}
            aria-haspopup="dialog"
            onClick={onDelete}
          >
            {deleting ? "Removing..." : "Remove"}
          </button>
        </div>
        {testResult ? (
          <p
            className={`mm-quiet-table__sub ${
              testResult.ok
                ? "mm-status-text--healthy"
                : "mm-status-text--failed"
            }`}
            role="alert"
          >
            {testResult.ok
              ? "Test notification sent successfully."
              : `Test failed: ${testResult.error ?? "Unknown error"}`}
          </p>
        ) : null}
      </td>
    </tr>
  );
}

export function AlertsTab() {
  const channelsQ = useNotificationChannelsQuery();
  const createMutation = useCreateNotificationChannelMutation();
  const updateMutation = useUpdateNotificationChannelMutation();
  const deleteMutation = useDeleteNotificationChannelMutation();
  const testMutation = useTestNotificationChannelMutation();

  const [showAddForm, setShowAddForm] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [deletingId, setDeletingId] = useState<number | null>(null);
  // Remove asks first. `pendingDelete` is the channel the dialog is asking about — it holds the
  // whole channel, not just the id, so the dialog can say which one by name.
  const [pendingDelete, setPendingDelete] =
    useState<NotificationChannelOut | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [testingId, setTestingId] = useState<number | null>(null);
  const [testResults, setTestResults] = useState<
    Record<number, { ok: boolean; error: string | null }>
  >({});

  const supportedEvents = [
    ...EVENT_ORDER.filter((e) =>
      (channelsQ.data?.supported_events ?? EVENT_ORDER).includes(e),
    ),
    ...(channelsQ.data?.supported_events ?? []).filter(
      (e) => !EVENT_ORDER.includes(e),
    ),
  ];
  const [toggleError, setToggleError] = useState<string | null>(null);

  // A channel with no events would never send anything, so the last one stays ticked.
  const handleToggle = async (
    channel: NotificationChannelOut,
    event: string,
  ) => {
    const events = channel.events.includes(event)
      ? channel.events.filter((e) => e !== event)
      : [...channel.events, event];
    if (events.length === 0) {
      setToggleError(
        `${channel.label} needs at least one event. Switch the channel off in Edit instead.`,
      );
      return;
    }
    setToggleError(null);
    try {
      await updateMutation.mutateAsync({
        id: channel.id,
        data: {
          label: channel.label,
          provider: channel.provider,
          url: channel.url,
          events,
          enabled: channel.enabled,
        },
      });
    } catch (err) {
      setToggleError(errorMessage(err, "That change could not be saved."));
    }
  };

  const handleCreate = async (data: NotificationFormData) => {
    await createMutation.mutateAsync(data);
    setShowAddForm(false);
  };

  const handleUpdate = async (id: number, data: NotificationFormData) => {
    await updateMutation.mutateAsync({ id, data });
    setEditingId(null);
  };

  const handleDelete = async (id: number) => {
    setDeletingId(id);
    setDeleteError(null);
    try {
      await deleteMutation.mutateAsync(id);
      setPendingDelete(null);
    } catch (err) {
      setDeleteError(errorMessage(err, "Could not remove this channel."));
    } finally {
      setDeletingId(null);
    }
  };

  const handleTest = async (id: number) => {
    setTestingId(id);
    setTestResults((prev) => {
      const next = { ...prev };
      delete next[id];
      return next;
    });
    try {
      const result = await testMutation.mutateAsync(id);
      setTestResults((prev) => ({ ...prev, [id]: result }));
    } catch (err) {
      setTestResults((prev) => ({
        ...prev,
        [id]: {
          ok: false,
          error: errorMessage(err, "Unknown error"),
        },
      }));
    } finally {
      setTestingId(null);
    }
  };

  const channels = channelsQ.data?.items ?? [];

  return (
    <div data-testid="suite-settings-notifications" className="mm-quiet-stack">
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

      <QuietSection
        level={3}
        headingId="suite-settings-notifications-heading"
        heading="Channels"
        aside={
          !showAddForm && editingId === null ? (
            <button
              type="button"
              className="mm-quiet-link"
              onClick={() => setShowAddForm(true)}
            >
              Add a channel →
            </button>
          ) : null
        }
      >
        <p className="mm-quiet-note">
          Tick what each channel should hear about; a change saves straight
          away. Use &ldquo;Send test&rdquo; before relying on a new channel.
        </p>
        {toggleError ? (
          <p className="mm-status-text--failed mt-2 text-sm" role="alert">
            {toggleError}
          </p>
        ) : null}

        {channelsQ.isLoading ? (
          <p className="mm-quiet-note mt-4">Loading channels...</p>
        ) : channelsQ.isError ? (
          <p
            className="mt-4 text-sm text-[var(--mm-status-failed-text)]"
            role="alert"
          >
            {errorMessage(
              channelsQ.error,
              "Could not load notification channels.",
            )}
          </p>
        ) : (
          <>
            {channels.length === 0 && !showAddForm ? (
              <p className="mm-quiet-note mt-4">
                No notification channels configured yet.
              </p>
            ) : null}
            {channels.length > 0 ? (
              <div className="mm-quiet-table-wrap mt-4">
                <table className="mm-quiet-table">
                  <thead>
                    <tr>
                      <th scope="col">Channel</th>
                      {supportedEvents.map((event) => (
                        <th key={event} scope="col" className="mm-alerts-cell">
                          {EVENT_LABELS[event] ?? event}
                        </th>
                      ))}
                      <th scope="col">
                        <span className="sr-only">Actions</span>
                      </th>
                    </tr>
                  </thead>
                  <tbody>
                    {channels.map((channel) =>
                      editingId === channel.id ? (
                        <tr key={channel.id}>
                          <td
                            colSpan={supportedEvents.length + 2}
                            data-label=""
                          >
                            <QuietFieldGroup title="Edit channel">
                              <ChannelForm
                                initial={{
                                  label: channel.label,
                                  provider: channel.provider as "webhook",
                                  url: channel.url,
                                  events: channel.events,
                                  enabled: channel.enabled,
                                }}
                                supportedEvents={supportedEvents}
                                onSave={(data) =>
                                  handleUpdate(channel.id, data)
                                }
                                onCancel={() => setEditingId(null)}
                                saving={updateMutation.isPending}
                                saveError={
                                  updateMutation.isError
                                    ? errorMessage(
                                        updateMutation.error,
                                        "Could not save.",
                                      )
                                    : null
                                }
                              />
                            </QuietFieldGroup>
                          </td>
                        </tr>
                      ) : (
                        <ChannelRow
                          key={channel.id}
                          channel={channel}
                          events={supportedEvents}
                          onToggleEvent={(event) =>
                            void handleToggle(channel, event)
                          }
                          toggling={updateMutation.isPending}
                          onEdit={() => setEditingId(channel.id)}
                          onDelete={() => {
                            setDeleteError(null);
                            setPendingDelete(channel);
                          }}
                          onTest={() => void handleTest(channel.id)}
                          testing={testingId === channel.id}
                          testResult={testResults[channel.id] ?? null}
                          deleting={deletingId === channel.id}
                        />
                      ),
                    )}
                  </tbody>
                </table>
              </div>
            ) : null}
          </>
        )}

        {showAddForm ? (
          <QuietFieldGroup title="New channel" className="mt-6">
            <ChannelForm
              supportedEvents={supportedEvents}
              onSave={handleCreate}
              onCancel={() => setShowAddForm(false)}
              saving={createMutation.isPending}
              saveError={
                createMutation.isError
                  ? errorMessage(
                      createMutation.error,
                      "Could not create channel.",
                    )
                  : null
              }
            />
          </QuietFieldGroup>
        ) : null}
      </QuietSection>

      {pendingDelete ? (
        <ConfirmDialog
          testId="notification-channel-remove-confirm"
          title={`Remove ${pendingDelete.label}?`}
          description={
            <>
              <p>
                Weir will stop sending job notifications to{" "}
                {pendingDelete.label}. Its address and the events it listens for
                go with it.
              </p>
              <p>Other channels keep working. This cannot be undone.</p>
            </>
          }
          confirmLabel="Remove channel"
          busy={deletingId === pendingDelete.id}
          error={deleteError}
          onCancel={() => {
            setDeleteError(null);
            setPendingDelete(null);
          }}
          onConfirm={() => void handleDelete(pendingDelete.id)}
        />
      ) : null}
    </div>
  );
}
