import type {
  LibraryModeSchedule,
  LibraryScanInfo,
} from "../../lib/processing/library-mode-api";
import { useTriggerLibraryScan } from "../../lib/processing/library-mode-queries";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { nextScheduled, scanned } from "./library-model";

/** Beside the title: when the numbers were counted, when the schedule runs next, and a way to count again. */
export function LibraryScanStatus({
  libraryId,
  scan,
  schedule,
  now,
}: {
  libraryId: number;
  scan: LibraryScanInfo | null;
  schedule: LibraryModeSchedule | undefined;
  now: number;
}) {
  const formatDate = useAppDateFormatter();
  const rescan = useTriggerLibraryScan(libraryId);
  const scheduleLine = nextScheduled(schedule, now, formatDate);
  return (
    <div className="mm-library-scan" data-testid="library-scan">
      {scan?.running ? (
        <>
          <i className="mm-live-pulse" aria-hidden="true" />
          <span>Checking this library now</span>
        </>
      ) : (
        <>
          <span className="mm-library-scan__dot" aria-hidden="true" />
          <span>{scanned(scan?.generated_at ?? null, now)}</span>
        </>
      )}
      {scheduleLine ? (
        <span data-testid="library-schedule">· {scheduleLine}</span>
      ) : null}
      <button
        type="button"
        className="mm-head-control"
        disabled={rescan.isPending || Boolean(scan?.running)}
        onClick={() => rescan.mutate()}
      >
        Check again
      </button>
    </div>
  );
}
