import { useState } from "react";

import { useTestDownloadClientConnection } from "../../../lib/download-clients/queries";
import { useTestMediaManagerConnection } from "../../../lib/media-managers/queries";
import { plural } from "../../../lib/ui/mm-plural";
import type { Health } from "./use-health";

export type CheckNow = {
  run: () => void;
  pending: boolean;
  /** What the last check found, in a sentence. Null before the first one. */
  notice: string | null;
};

function noticeAfter(answers: readonly boolean[]): string {
  if (answers.length === 0) return "Folders checked.";
  const silent = answers.filter((answered) => !answered).length;
  const checked = `Folders and ${plural(answers.length, "connection", "connections")} checked`;
  return silent === 0
    ? `${checked}: all answered.`
    : `${checked}: ${silent} did not answer.`;
}

/**
 * Re-runs what Health shows: each workflow's folder chain is read again, and every switched-on media manager
 * and download client is tested now, which also stores its answer on the connection.
 */
export function useCheckNow(
  health: Pick<Health, "managers" | "downloadClients" | "recheckFolders">,
): CheckNow {
  const testManager = useTestMediaManagerConnection();
  const testClient = useTestDownloadClientConnection();
  const [pending, setPending] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);

  const run = () => {
    const answered = (test: Promise<{ ok: boolean }>) =>
      test.then(
        (result) => result.ok,
        () => false,
      );
    const tests = [
      ...health.managers
        .filter((manager) => manager.enabled)
        .map((manager) => answered(testManager.mutateAsync(manager.id))),
      ...health.downloadClients
        .filter((client) => client.enabled)
        .map((client) => answered(testClient.mutateAsync(client.id))),
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
