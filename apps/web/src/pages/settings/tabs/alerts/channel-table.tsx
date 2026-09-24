import { useState } from "react";

import { QuietFieldGroup } from "../../../../components/shared/quiet-section";
import { errorMessage } from "../../../../lib/api/error-message";
import {
  useTestNotificationChannelMutation,
  useUpdateNotificationChannelMutation,
} from "../../../../lib/settings/queries";
import type { NotificationChannelOut } from "../../../../lib/settings/types";
import { eventLabel } from "./alert-events";
import { ChannelForm, type ChannelFormData } from "./channel-form";
import { ChannelRow, type TestResult } from "./channel-row";

function formDataOf(channel: NotificationChannelOut): ChannelFormData {
  return {
    label: channel.label,
    provider: channel.provider,
    url: channel.url,
    events: channel.events,
    enabled: channel.enabled,
  };
}

/** Sends a test to one channel at a time and keeps each channel's last answer beside it. */
function useChannelTests() {
  const testMutation = useTestNotificationChannelMutation();
  const [results, setResults] = useState<Record<number, TestResult>>({});
  const test = (id: number) => {
    setResults((current) => {
      const next = { ...current };
      delete next[id];
      return next;
    });
    testMutation.mutate(id, {
      onSuccess: (result) =>
        setResults((current) => ({ ...current, [id]: result })),
      onError: (err) =>
        setResults((current) => ({
          ...current,
          [id]: { ok: false, error: errorMessage(err, "Unknown error") },
        })),
    });
  };
  const testingId = testMutation.isPending
    ? (testMutation.variables ?? null)
    : null;
  return { results, testingId, test };
}

/**
 * Every channel with a tick box per event; a tick saves straight away. The channel being edited
 * turns into its form in place.
 */
export function ChannelTable({
  channels,
  supportedEvents,
  editingId,
  deletingId,
  onEdit,
  onRemove,
}: {
  channels: NotificationChannelOut[];
  supportedEvents: string[];
  editingId: number | null;
  deletingId: number | null;
  onEdit: (id: number | null) => void;
  onRemove: (channel: NotificationChannelOut) => void;
}) {
  const updateMutation = useUpdateNotificationChannelMutation();
  const tests = useChannelTests();
  const [toggleError, setToggleError] = useState<string | null>(null);

  // A channel with no events would never send anything, so the last one stays ticked.
  const toggle = (channel: NotificationChannelOut, event: string) => {
    const events = channel.events.includes(event)
      ? channel.events.filter((e) => e !== event)
      : [...channel.events, event];
    if (events.length === 0) {
      setToggleError(
        `${channel.label} needs at least one event. Switch the alert off in Edit instead.`,
      );
      return;
    }
    setToggleError(null);
    updateMutation.mutate(
      { id: channel.id, data: { ...formDataOf(channel), events } },
      {
        onError: (err) =>
          setToggleError(errorMessage(err, "That change could not be saved.")),
      },
    );
  };
  const save = (id: number, data: ChannelFormData) =>
    updateMutation.mutate({ id, data }, { onSuccess: () => onEdit(null) });

  return (
    <>
      {toggleError ? (
        <p className="mm-status-text--failed mt-2 text-sm" role="alert">
          {toggleError}
        </p>
      ) : null}
      <div className="mm-quiet-table-wrap mt-4">
        <table className="mm-quiet-table">
          <thead>
            <tr>
              <th scope="col">Channel</th>
              {supportedEvents.map((event) => (
                <th key={event} scope="col" className="mm-alerts-cell">
                  {eventLabel(event)}
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
                  <td colSpan={supportedEvents.length + 2} data-label="">
                    <QuietFieldGroup title="Edit alert">
                      <ChannelForm
                        initial={formDataOf(channel)}
                        supportedEvents={supportedEvents}
                        onSave={(data) => save(channel.id, data)}
                        onCancel={() => onEdit(null)}
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
                  onToggleEvent={(event) => toggle(channel, event)}
                  toggling={updateMutation.isPending}
                  onEdit={() => onEdit(channel.id)}
                  onDelete={() => onRemove(channel)}
                  onTest={() => tests.test(channel.id)}
                  testing={tests.testingId === channel.id}
                  testResult={tests.results[channel.id] ?? null}
                  deleting={deletingId === channel.id}
                />
              ),
            )}
          </tbody>
        </table>
      </div>
    </>
  );
}
