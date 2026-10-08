import { useLiveUpdatesPaused } from "../../lib/live/use-live-updates-paused";

/**
 * Says so when the live connection to Weir is lost: what is on screen stops following Weir until it answers again, and
 * nothing may be shown as current without this. Nothing while all is well, while a page is still making its first
 * connection, or while a dropped one mends within moments. Weir not answering is broken, so it is drawn in the broken
 * colour.
 */
export function LiveConnectionBanner() {
  const paused = useLiveUpdatesPaused();
  return (
    <div role="status">
      {paused ? (
        <p
          className="mm-live-banner mm-status-text"
          data-status="broken"
          data-testid="live-connection-banner"
        >
          Live updates paused: can&apos;t reach Weir. Reconnecting…
        </p>
      ) : null}
    </div>
  );
}
