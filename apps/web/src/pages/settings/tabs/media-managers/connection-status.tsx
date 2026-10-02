import { Link } from "react-router-dom";

import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";
import { joinNames } from "../../../../lib/processing/workflow-kind";
import { connectionTitle } from "../../../../lib/ui/connection-title";
import { workflowFromManagerPath } from "../../../../lib/settings/setup-areas";
import { ConnectionStatusLine, lastResult } from "./connection-health";

type Formatter = (iso: string | null) => string;

/**
 * Whether it answers, when it was last checked, and why not when it does not. What an operator wants to know
 * here is "is it connected", not what the endpoint said, so a good result is one word. Weir checks every
 * enabled manager each minute, so this is how it is now, not when someone last pressed Test.
 */
export function ConnectionStatus({
  connection,
  fmt,
}: {
  connection: MediaManagerConnection;
  fmt: Formatter;
}) {
  const unchecked = connection.enabled && lastResult(connection) === null;
  return (
    <ConnectionStatusLine
      connection={connection}
      fmt={fmt}
      unchecked="Checking…"
      testId="media-manager-status"
    >
      {unchecked ? (
        <p className="mm-conn-row__problem">
          Weir checks it within a minute, or press Test.
        </p>
      ) : null}
    </ConnectionStatusLine>
  );
}

/**
 * One warning for the whole list, naming each manager that has no secret protecting its webhook (#701). The
 * field's own string content is never shown; its presence is only the signal. The fix is in each manager's own
 * setup details, which say so.
 */
export function UnsignedWebhookBanner({
  connections,
}: {
  connections: readonly MediaManagerConnection[];
}) {
  const unsigned = connections.filter(
    (connection) => connection.unsigned_webhook_warning !== null,
  );
  if (unsigned.length === 0) return null;
  const names = joinNames(unsigned.map(connectionTitle));
  return (
    <p
      className="mm-conn-banner mm-status-text"
      data-status="attention"
      role="alert"
      data-testid="media-manager-unsigned-webhook-warning"
    >
      {unsigned.length === 1
        ? `${names} accepts webhooks without a secret. Create one under "How to point it at Weir", then add it to ${names}.`
        : `${names} accept webhooks without a secret. Create one for each under "How to point it at Weir", then add it to that manager.`}
    </p>
  );
}

/** Managers Weir can read folders from, and so can offer to start a workflow from. */
const OFFERS_WORKFLOWS: MediaManagerConnection["kind"][] = [
  "deluno",
  "sonarr",
  "radarr",
];

/**
 * The workflows this connection feeds, so it is plain what depends on it, and a way to start one from what it
 * reports. A workflow linked to it is fed by it; a Weir only workflow is not, and is listed on its own.
 */
export function FedWorkflows({
  connection,
}: {
  connection: MediaManagerConnection;
}) {
  const libraries = useProcessingLibrariesQuery();
  if (!libraries.data) return null;
  const fed = libraries.data.filter((library) =>
    library.manager_connection_ids.includes(connection.id),
  );
  return (
    <div className="mm-conn-row__feeds">
      <p data-testid="media-manager-libraries">
        {fed.length > 0 ? (
          <>
            Workflows it feeds:{" "}
            <span className="mm-conn-row__when">
              {fed.map((library) => library.name).join(", ")}
            </span>
          </>
        ) : (
          "No workflow is linked to it yet."
        )}
      </p>
      {OFFERS_WORKFLOWS.includes(connection.kind) ? (
        <Link
          className="mm-quiet-link"
          to={workflowFromManagerPath(connection.id)}
          aria-label={`Add a workflow from ${connectionTitle(connection)}`}
        >
          Add a workflow
        </Link>
      ) : null}
    </div>
  );
}
