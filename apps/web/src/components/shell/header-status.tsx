import { usePauseQuery } from "../../lib/pause/pause-queries";
import { useSystemReadinessQuery } from "../../lib/system/readiness-queries";
import { Chip } from "../panels/chip";
import { PausePill } from "./pause-pill";

/**
 * The header's status: nothing while all is well, so a pill is always news. Weir not answering comes
 * first, since nothing else on screen can be trusted then; a pause says how it ends, so nobody has
 * to guess how long it will last.
 */
export function HeaderStatus() {
  const pause = usePauseQuery();
  const readiness = useSystemReadinessQuery();

  let status = null;
  if (readiness.isError) {
    status = (
      <Chip
        meaning="broken"
        data-testid="status-offline"
        title="Weir is not answering. Check that it is running."
      >
        Can&apos;t reach Weir
      </Chip>
    );
  } else if (pause.data?.paused) {
    status = <PausePill pause={pause.data} />;
  }

  return (
    <div className="mm-header-status" role="status">
      {status}
    </div>
  );
}
