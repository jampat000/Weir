import type { NotificationChannelOut } from "../../../../lib/settings/types";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { eventLabel } from "./alert-events";
import { ALERT_CHECKBOX_CLASS } from "./channel-form";
import { maskWebhookUrl } from "./webhook-url-mask";

export type TestResult = { ok: boolean; error: string | null };

function TestOutcome({ result }: { result: TestResult }) {
  return (
    <p
      className={`mm-quiet-table__sub ${
        result.ok ? "mm-status-text--healthy" : "mm-status-text--failed"
      }`}
      role="alert"
    >
      {result.ok
        ? "Test alert sent successfully."
        : `Test failed: ${result.error ?? "Unknown error"}`}
    </p>
  );
}

/** One channel as a table row. The hairline under it is what says a Remove button
 *  belongs to this channel and not its neighbour. */
export function ChannelRow({
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
}: {
  channel: NotificationChannelOut;
  events: string[];
  onToggleEvent: (event: string) => void;
  toggling: boolean;
  onEdit: () => void;
  onDelete: () => void;
  onTest: () => void;
  testing: boolean;
  testResult: TestResult | null;
  deleting: boolean;
}) {
  return (
    <tr>
      <th scope="row" className="mm-quiet-table__name">
        <span>{channel.label}</span>
        <span className="mm-quiet-badge">{channel.provider}</span>
        {!channel.enabled ? (
          <span className="mm-quiet-badge mm-quiet-badge--off">Disabled</span>
        ) : null}
        <span className="mm-quiet-table__sub font-mono">
          {maskWebhookUrl(channel.url)}
        </span>
      </th>
      {events.map((event) => (
        <td
          key={event}
          data-label={eventLabel(event)}
          className="mm-alerts-cell"
        >
          <input
            type="checkbox"
            className={ALERT_CHECKBOX_CLASS}
            aria-label={`${channel.label}: ${eventLabel(event)}`}
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
            {testing ? "Testing…" : "Send test"}
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
            {deleting ? "Removing…" : "Remove"}
          </button>
        </div>
        {testResult ? <TestOutcome result={testResult} /> : null}
      </td>
    </tr>
  );
}
