import type { MediaManagerConnection } from "../../../../lib/media-managers/media-managers-api";
import { useProcessingLibrariesQuery } from "../../../../lib/processing/libraries-queries";

type Formatter = (iso: string | null) => string;

/**
 * A result only counts with a time behind it. "Connected" above "Last checked: never" is two
 * sentences that cannot both be true, so a result with no check time reads as unchecked.
 */
function lastResult(connection: MediaManagerConnection): boolean | null {
  return connection.last_test_at ? connection.last_test_ok : null;
}

/**
 * Weir checks every enabled manager each minute, so this is how it is now, not when someone last
 * pressed Test.
 */
function headline(connection: MediaManagerConnection): string {
  const result = lastResult(connection);
  if (result === null) return connection.enabled ? "Checking…" : "Off";
  return result ? "Answering" : "Not answering";
}

/** The site's status colours, so the headline reads like every other good or bad word in Weir. */
function tone(result: boolean | null): string {
  if (result === null) return "text-mm-text";
  return result ? "mm-status-text--healthy" : "mm-status-text--failed";
}

/**
 * A plain headline, when it was last checked, and the detail underneath. What an operator wants to
 * know here is "is it connected", not what the endpoint said.
 */
export function ConnectionStatusPanel({
  connection,
  fmt,
}: {
  connection: MediaManagerConnection;
  fmt: Formatter;
}) {
  const result = lastResult(connection);
  return (
    <div
      className="mt-3 text-sm text-mm-text2"
      data-testid="media-manager-status"
    >
      <p className={`text-sm font-medium ${tone(result)}`}>
        {headline(connection)}
      </p>
      <p className="mt-1 text-xs text-mm-text2">
        Last checked:{" "}
        <span className="font-medium text-mm-text">
          {connection.last_test_at ? fmt(connection.last_test_at) : "never"}
        </span>
        {connection.enabled ? " · Weir checks every minute" : ""}
      </p>
      {result === false && connection.last_test_detail ? (
        <p className="mm-status-text--failed mt-1 text-xs">
          {connection.last_test_detail}
        </p>
      ) : null}
      {result === null && connection.enabled ? (
        <p className="mt-2 text-xs text-mm-text2">
          Weir checks it within a minute, or press Test connection.
        </p>
      ) : null}
    </div>
  );
}

/** The libraries linked to this app, so it is plain what depends on it. */
export function LinkedLibraries({ connectionId }: { connectionId: number }) {
  const libraries = useProcessingLibrariesQuery();
  if (!libraries.data) return null;
  const linked = libraries.data.filter((library) =>
    library.manager_connection_ids.includes(connectionId),
  );
  return (
    <p
      className="mt-2 text-xs text-mm-text2"
      data-testid="media-manager-libraries"
    >
      {linked.length > 0 ? (
        <>
          Libraries:{" "}
          <span className="font-medium text-mm-text">
            {linked.map((library) => library.name).join(", ")}
          </span>
        </>
      ) : (
        "No library uses it yet. Link one in Settings › Libraries."
      )}
    </p>
  );
}
