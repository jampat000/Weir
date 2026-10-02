import { useState } from "react";

import { useConnectionTest } from "../../../lib/connections/use-connection-test";
import type { Health } from "./use-health";

export type CheckNow = {
  run: () => void;
  pending: boolean;
  /** What the last check found, in a few words. Null before the first one. */
  notice: string | null;
};

function noticeAfter(answers: readonly boolean[]): string {
  if (answers.length === 0) return "Folders checked.";
  const silent = answers.filter((answered) => !answered).length;
  return silent === 0
    ? "Checked · all answered."
    : `Checked · ${silent} didn't answer.`;
}

/**
 * Re-runs what Health shows: each workflow's folder chain is read again, and every switched-on media manager
 * and download client is tested now, which also stores its answer on the connection.
 */
export function useCheckNow(
  health: Pick<Health, "managers" | "downloadClients" | "recheckFolders">,
): CheckNow {
  const testConnection = useConnectionTest();
  const [pending, setPending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const run = () => {
    const tests = [
      ...health.managers
        .filter((manager) => manager.enabled)
        .map((manager) =>
          testConnection({ kind: "media_manager", id: manager.id }),
        ),
      ...health.downloadClients
        .filter((client) => client.enabled)
        .map((client) =>
          testConnection({ kind: "download_client", id: client.id }),
        ),
    ];
    setPending(true);
    setNotice(null);
    void Promise.all([Promise.all(tests), health.recheckFolders()])
      .then(([answers]) => setNotice(noticeAfter(answers)))
      .finally(() => setPending(false));
  };
  return { run, pending, notice };
}

/** The "Check now" action for a Health header. */
export function CheckNowButton({ check }: { check: CheckNow }) {
  return (
    <button
      type="button"
      className="mm-health__action"
      title="Reads every workflow's folders again and tests every media manager and download client now."
      disabled={check.pending}
      onClick={check.run}
    >
      {check.pending ? "Checking…" : "Check now"}
    </button>
  );
}
