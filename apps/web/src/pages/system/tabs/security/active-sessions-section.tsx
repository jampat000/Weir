import { useState } from "react";

import { QuietSection } from "../../../../components/shared/quiet-section";
import type { ActiveSession } from "../../../../lib/api/types";
import {
  useActiveSessionsQuery,
  useRevokeOtherSessionsMutation,
  useRevokeSessionMutation,
} from "../../../../lib/auth/queries";
import { mmActionButtonClass } from "../../../../lib/ui/mm-control-roles";
import { useAppDateFormatter } from "../../../../lib/ui/mm-format-date";

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
    <div className="mm-quiet-table-wrap mt-4">
      <table className="mm-quiet-table" data-testid="active-sessions">
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
              <th scope="row" className="mm-quiet-table__name">
                <span>{session.client_label || "Browser session"}</span>
                {session.current ? (
                  <span className="mm-quiet-badge">This browser</span>
                ) : null}
                {session.trusted_device ? (
                  <span className="mm-quiet-badge">Trusted</span>
                ) : null}
              </th>
              <td data-label="Last seen">{formatDate(session.last_seen_at)}</td>
              <td data-label="Expires">
                {formatDate(session.absolute_expires_at)}
              </td>
              <td data-label="">
                {/* Signing a session out is immediate and cannot be undone, so it is a real button. */}
                <button
                  type="button"
                  className={mmActionButtonClass({ variant: "tertiary" })}
                  disabled={session.current || revoking}
                  onClick={() => onRevoke(session.session_id)}
                >
                  {revoking ? "Signing out…" : "Sign out"}
                </button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
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
    <QuietSection
      level={3}
      headingId="suite-security-sessions-heading"
      heading="Active sessions"
      aside={
        <button
          type="button"
          className="mm-quiet-link"
          disabled={
            revokeOthers.isPending || sessionsQ.isPending || others.length === 0
          }
          onClick={signOutOthers}
        >
          {revokeOthers.isPending
            ? "Signing out…"
            : "Sign out other sessions →"}
        </button>
      }
    >
      <p className="mm-quiet-note">
        Review signed-in browsers and sign out anything you no longer recognize.
        Session tokens are never shown.
      </p>
      {status ? (
        <p className="mm-quiet-note mt-3" role="status">
          {status}
        </p>
      ) : null}
      {sessionsQ.isError ? (
        <p className="mt-4 text-sm text-mm-status-failed-text" role="alert">
          Could not load active sessions. Refresh the page to try again.
        </p>
      ) : sessionsQ.isPending ? (
        <p className="mm-quiet-note mt-4">Loading active sessions…</p>
      ) : sessions.length === 0 ? (
        <p className="mm-quiet-note mt-4">No active sessions were found.</p>
      ) : (
        <SessionsTable
          sessions={sessions}
          revoking={revokeSession.isPending}
          onRevoke={signOut}
        />
      )}
    </QuietSection>
  );
}
