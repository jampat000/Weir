import { usePauseQuery } from "../../lib/pause/pause-queries";
import { useSystemReadinessQuery } from "../../lib/system/readiness-queries";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { Chip } from "../panels/chip";

/**
 * The header's status: nothing while all is well, so a pill is always news. Weir not answering comes
 * first, since nothing else on screen can be trusted then; a pause says when it lifts, so nobody has
 * to guess how long it will last.
 */
export function HeaderStatus() {
  const pause = usePauseQuery();
  const readiness = useSystemReadinessQuery();
  const formatDate = useAppDateFormatter();

  let status = null;
  if (readiness.isError) {
    status = (
      <Chip
        tone="failed"
        data-testid="status-offline"
        title="Weir is not answering. Check that it is running."
      >
        Can&apos;t reach Weir
      </Chip>
    );
  } else if (pause.data?.paused) {
    const until = pause.data.paused_until
      ? `until ${formatDate(pause.data.paused_until)}`
      : "until you resume";
    status = (
      <Chip tone="warning" data-testid="pause-badge" title={pause.data.reason}>
        Paused {until}
      </Chip>
    );
  }

  return (
    <div className="mm-header-status" role="status">
      {status}
    </div>
  );
}
