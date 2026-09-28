import { QuietSection } from "../../../../components/shared/quiet-section";
import { useNetworkAccessQuery } from "../../../../lib/settings/queries";
import type { NetworkAccessState } from "../../../../lib/settings/types";
import {
  mmStatusPillClass,
  type MmStatusTone,
} from "../../../../lib/ui/mm-status-tone";

function stateTone(state: NetworkAccessState): MmStatusTone {
  if (state === "allowed") return "healthy";
  if (state === "blocked") return "failed";
  return "neutral";
}

function stateLabel(state: NetworkAccessState): string {
  switch (state) {
    case "allowed":
      return "Reachable";
    case "blocked":
      return "Blocked";
    case "not_configured":
      return "Not set up";
    default:
      return state;
  }
}

/**
 * System › About: whether other devices on the network can reach Weir. Windows package only — the server
 * reports "not_applicable" everywhere else (Docker, a bare source install), and this renders nothing there,
 * rather than a card that never applies. Independent of every other card on the page: its own query, loads
 * and fails on its own.
 */
export function NetworkAccessSection() {
  const query = useNetworkAccessQuery();
  const status = query.data;

  if (status?.state === "not_applicable") {
    return null;
  }

  return (
    <QuietSection
      level={3}
      headingId="about-network-access-heading"
      heading="Network access"
      data-testid="about-network-access"
    >
      {query.isLoading ? (
        <p className="mm-quiet-note">Checking…</p>
      ) : status ? (
        <p
          className="mt-1 flex items-center gap-2 text-base font-semibold text-mm-text1"
          data-testid="about-network-access-status"
        >
          <span className={mmStatusPillClass(stateTone(status.state))}>
            {stateLabel(status.state)}
          </span>
          {status.summary}
        </p>
      ) : (
        <p className="mm-quiet-note">Could not check network access.</p>
      )}
    </QuietSection>
  );
}
