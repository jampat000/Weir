import type {
  NetworkAccessStatus,
  NetworkScope,
} from "../../../../lib/settings/types";
import type { MmStatusTone } from "../../../../lib/ui/mm-status-tone";

/** What the network access line says: a status pill, a few words after it, and whether trying again can help. */
export type NetworkAccessLine = {
  tone: MmStatusTone;
  label: string;
  /** The addresses other devices type; empty unless Weir is reachable. */
  addresses: readonly string[];
  /** A sentence under the line, when the person has something to do. */
  note: string | null;
  canRetry: boolean;
};

/**
 * The choice the control shows as selected: the one waiting for the tray when there is one, otherwise what the
 * running server does. A person who has just chosen sees their choice at once, not the old state until it catches up.
 */
export function intendedScope(status: NetworkAccessStatus): NetworkScope {
  return status.pending_scope ?? status.scope ?? "this_pc_only";
}

/**
 * The state in a few words. A pending change says what Weir is waiting for: the person's approval of Windows'
 * prompt on that PC, or only the restart.
 */
export function describeNetworkAccess(
  status: NetworkAccessStatus,
): NetworkAccessLine {
  const none = { addresses: [], note: null, canRetry: false };
  if (status.pending_scope === "network") {
    return status.firewall === "allowed"
      ? {
          ...none,
          tone: "info",
          label: "Restarting Weir for your network…",
        }
      : {
          ...none,
          tone: "warning",
          label: `Waiting for approval on ${status.machine_name}`,
          note: "Approve the Windows prompt on that PC. Weir restarts for your network once you do.",
        };
  }
  if (status.pending_scope === "this_pc_only") {
    return {
      ...none,
      tone: "info",
      label: "Restarting Weir for this PC only…",
    };
  }
  if (status.state === "allowed") {
    return {
      ...none,
      tone: "healthy",
      label: "Reachable from your network:",
      addresses: status.addresses,
    };
  }
  if (status.state === "blocked") {
    return {
      ...none,
      tone: "failed",
      label: "Blocked by Windows Firewall",
      canRetry: true,
    };
  }
  return { ...none, tone: "neutral", label: "This PC only" };
}
