import { Chip } from "../../../../components/panels/chip";
import { useNetworkAccessQuery } from "../../../../lib/settings/queries";
import type { NetworkAccessState } from "../../../../lib/settings/types";
import type { MmStatusTone } from "../../../../lib/ui/mm-status-tone";

function stateTone(state: NetworkAccessState): MmStatusTone {
  if (state === "allowed") return "healthy";
  if (state === "blocked") return "failed";
  return "neutral";
}

function stateLabel(state: NetworkAccessState): string {
  switch (state) {
    case "allowed":
      return "Your network";
    case "blocked":
      return "Blocked";
    case "this_pc_only":
      return "This PC only";
    default:
      return state;
  }
}

/** Where to change it, in the words the tray menu uses. The server's longer sentence is the hover note. */
function stateAdvice(state: NetworkAccessState): string {
  switch (state) {
    case "allowed":
      return "Change it from the tray icon → Only allow this PC.";
    case "blocked":
      return "Windows Firewall is blocking it. Fix it from the tray icon.";
    default:
      return "Change it from the tray icon → Allow other devices.";
  }
}

/**
 * The network half of System › About's "This PC" card: whether other devices on the network can reach Weir.
 * Windows package only — the server reports "not_applicable" everywhere else (Docker, a bare source install), and
 * this renders nothing there, rather than a half that never applies. Independent of everything else on the page:
 * its own query, loads and fails on its own.
 */
export function NetworkAccessHalf({ labelId }: { labelId: string }) {
  const query = useNetworkAccessQuery();
  const status = query.data;

  if (status?.state === "not_applicable") {
    return null;
  }

  return (
    <section
      className="mm-this-pc__half"
      aria-labelledby={labelId}
      data-testid="about-network-access"
    >
      <h4 id={labelId} className="mm-this-pc__label">
        Network
      </h4>
      <div title={status?.summary} data-testid="about-network-access-status">
        {query.isLoading ? (
          <p className="mm-sys-note">Checking…</p>
        ) : status ? (
          <>
            <p className="mm-this-pc__state">
              <Chip tone={stateTone(status.state)}>
                {stateLabel(status.state)}
              </Chip>
            </p>
            <p className="mm-sys-note">{stateAdvice(status.state)}</p>
          </>
        ) : (
          <p className="mm-sys-note">Could not check network access.</p>
        )}
      </div>
    </section>
  );
}
