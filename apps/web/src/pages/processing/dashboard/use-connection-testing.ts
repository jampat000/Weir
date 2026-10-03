import { useState } from "react";

import { noteConnectionActivity } from "../../../lib/connections/connection-lights";
import type { ConnectionEntry } from "../../../lib/connections/connection-model";
import { useConnectionTest } from "../../../lib/connections/use-connection-test";

export type ConnectionTesting = {
  /** The keys of the connections being tested now. */
  testing: ReadonlySet<string>;
  test: (entry: ConnectionEntry) => Promise<void>;
  /** Tests every connection that is switched on and has an address, all at once. */
  testAll: (entries: readonly ConnectionEntry[]) => Promise<void>;
  allBusy: boolean;
};

/**
 * The Test buttons on the System card: which connections are being tested now, and the same call Settings and "Check
 * now" use. A test lights its row as the server's own frames do, so the light shows even before they arrive.
 */
export function useConnectionTesting(): ConnectionTesting {
  const testConnection = useConnectionTest();
  const [testing, setTesting] = useState<ReadonlySet<string>>(new Set());
  const [allBusy, setAllBusy] = useState(false);

  const startTesting = (key: string) =>
    setTesting((current) => new Set(current).add(key));
  const stopTesting = (key: string) =>
    setTesting((current) => {
      const next = new Set(current);
      next.delete(key);
      return next;
    });

  const test = async (entry: ConnectionEntry) => {
    startTesting(entry.key);
    const answered = await testConnection(entry);
    noteConnectionActivity({
      kind: entry.kind,
      id: entry.id,
      phase: answered ? "answered" : "failed",
      direction: "outbound",
      at: new Date().toISOString(),
      ms: null,
    });
    stopTesting(entry.key);
  };

  const testAll = async (entries: readonly ConnectionEntry[]) => {
    setAllBusy(true);
    try {
      await Promise.all(
        entries.filter((entry) => entry.testable).map((entry) => test(entry)),
      );
    } finally {
      setAllBusy(false);
    }
  };

  return { testing, test, testAll, allBusy };
}
