import { QuietDisclosure } from "../../../../components/shared/quiet-section";
import type { useSecurityOverviewQuery } from "../../../../lib/settings/queries";
import { signInProtectionFacts } from "./security-facts";

/** The protections in force. Read-only: they come from startup configuration. */
export function SignInProtectionSection({
  overviewQ,
}: {
  overviewQ: ReturnType<typeof useSecurityOverviewQuery>;
}) {
  const overview = overviewQ.data;
  const facts = overview ? signInProtectionFacts(overview) : [];

  return (
    <QuietDisclosure
      title="How sign-in is protected"
      detail="Read-only here: these come from Weir's startup configuration and change only with a restart."
      summaryWhenClosed={
        overview ? `${facts.length} protections in force` : undefined
      }
      data-testid="suite-security-posture"
    >
      {overview ? (
        <div className="mm-quiet-table-wrap mt-4">
          <table className="mm-quiet-table">
            <thead>
              <tr>
                <th scope="col">Protection</th>
                <th scope="col">Setting</th>
              </tr>
            </thead>
            <tbody>
              {facts.map((fact) => (
                <tr key={fact.label}>
                  <th scope="row" className="mm-quiet-table__name">
                    {fact.label}
                  </th>
                  <td data-label="Setting" className={fact.toneClass}>
                    {fact.value}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : overviewQ.isError ? (
        <p className="mt-4 text-sm text-mm-status-failed-text" role="alert">
          Could not load the server security overview. Check the server logs and
          try again.
        </p>
      ) : (
        <p className="mm-quiet-note mt-4">Loading server security overview…</p>
      )}
      {overview?.restart_required_note ? (
        <p className="mm-quiet-note mt-4">{overview.restart_required_note}</p>
      ) : null}
    </QuietDisclosure>
  );
}
