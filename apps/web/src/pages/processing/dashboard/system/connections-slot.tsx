import { useConnections } from "../../../../lib/connections/use-connections";
import type { ProcessingLibrary } from "../../../../lib/processing/libraries-api";
import { useNow } from "../../../../lib/ui/use-now";
import { ConnectionsCard } from "../connections-card";
import { useConnectionTesting } from "../use-connection-testing";
import { useHealth } from "../use-health";

/** Once a second, so "checked 12s ago" moves. */
const TICK_MS = 1000;

type ConnectionsSlotProps = {
  workflows: readonly ProcessingLibrary[];
  /** Narrows the card to the media managers and download clients one workflow uses. */
  workflowId: number | null;
};

/** The Connections card with what it needs: the connections in scope, how each answers now, and the tests. */
export function ConnectionsSlot({
  workflows,
  workflowId,
}: ConnectionsSlotProps) {
  const health = useHealth(workflows, workflowId);
  const now = useNow(TICK_MS);
  const { entries, lights } = useConnections(
    health.managers,
    health.downloadClients,
  );
  const testing = useConnectionTesting();
  return (
    <ConnectionsCard
      entries={entries}
      lights={lights}
      testing={testing}
      now={now}
    />
  );
}
