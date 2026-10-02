import { useState } from "react";

import { Chip } from "../../../../components/panels/chip";
import { Panel } from "../../../../components/panels/panel";
import { LoadError } from "../../../../components/shared/load-error";
import type { ActiveSession } from "../../../../lib/api/types";
import {
  useActiveSessionsQuery,
  useRevokeOtherSessionsMutation,
  useRevokeSessionMutation,
} from "../../../../lib/auth/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";

const SESSIONS_NOTE =
  "Browsers signed in to Weir. Session tokens are never shown.";
const SESSIONS_DETAIL =
  "Review signed-in browsers and sign out anything you no longer recognize. Session tokens are never shown.";

function SessionsTable({
  sessions,
  revoking,
  onRevoke,
}: {
  sessions: ActiveSession[];
  revoking: boolean;
  onRevoke: (sessionId: string) => void;
}) {
  const formatDate = useAppDateFormatter();
  return (
    <table
      className="mm-quiet-table mm-sys-table"
      data-testid="active-sessions"
    >
      <thead>
        <tr>
          <th scope="col">Browser</th>
          <th scope="col">Last seen</th>
          <th scope="col">Expires</th>
          <th scope="col">
            <span className="sr-only">Sign out</span>
          </th>
        </tr>
      </thead>
      <tbody>
        {sessions.map((session) => (
          <tr key={session.session_id}>
            <th scope="row">
              <span className="mm-sys-table__who">
                {session.client_label || "Browser session"}
                {session.current ? <Chip tone="info">This browser</Chip> : null}
                {session.trusted_device ? (
                  <Chip tone="healthy">Trusted</Chip>
                ) : null}
              </span>
            </th>
            <td>{formatDate(session.last_seen_at)}</td>
            <td>{formatDate(session.absolute_expires_at)}</td>
            <td>
              {/* The browser in use is signed out from the user menu, so it has no button here. */}
              {session.current ? null : (
                <button
                  type="button"
                  className={`${mmActionButtonClass({ variant: "secondary" })} mm-sys-btn`}
                  disabled={revoking}
                  onClick={() => onRevoke(session.session_id)}
                >
                  {revoking ? "Signing out…" : "Sign out"}
                </button>
              )}
            </td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

/** Every browser signed in to Weir, with a way to sign out the ones you do not recognise. */
export function ActiveSessionsSection({ enabled }: { enabled: boolean }) {
  const sessionsQ = useActiveSessionsQuery(enabled);
  const revokeOthers = useRevokeOtherSessionsMutation();
  const revokeSession = useRevokeSessionMutation();
  const [status, setStatus] = useState<string | null>(null);
  const sessions = sessionsQ.data ?? [];
  const others = sessions.filter((session) => !session.current);

  const signOutOthers = () => {
    setStatus(null);
    revokeOthers.mutate(undefined, {
      onSuccess: (result) => setStatus(result.message),
      onError: () =>
        setStatus(
          "Could not sign out the other sessions. Refresh and try again.",
        ),
    });
  };
  const signOut = (sessionId: string) => {
    setStatus(null);
    revokeSession.mutate(sessionId, {
      onSuccess: (result) => setStatus(result.message),
      onError: () =>
        setStatus(
          "Could not sign out that session. It may already be inactive.",
        ),
    });
  };

  return (
    <Panel
      title="Active sessions"
      headingId="suite-security-sessions-heading"
      headingLevel={3}
      padded
      note={<span title={SESSIONS_DETAIL}>{SESSIONS_NOTE}</span>}
      aside={
        others.length > 0 ? (
          <button
            type="button"
            className={`${mmActionButtonClass({ variant: "danger-outline" })} mm-sys-btn`}
            disabled={revokeOthers.isPending}
            onClick={signOutOthers}
          >
            {revokeOthers.isPending
              ? "Signing out…"
              : "Sign out other sessions"}
          </button>
        ) : null
      }
    >
      {sessionsQ.isError ? (
        <LoadError thing="active sessions" error={sessionsQ.error} />
      ) : sessionsQ.isPending ? (
        <p className="mm-quiet-note">Loading active sessions…</p>
      ) : sessions.length === 0 ? (
        <p className="mm-quiet-note">No active sessions were found.</p>
      ) : (
        <SessionsTable
          sessions={sessions}
          revoking={revokeSession.isPending}
          onRevoke={signOut}
        />
      )}
      {status ? (
        <p className="mm-sys-note" role="status">
          {status}
        </p>
      ) : null}
    </Panel>
  );
}
