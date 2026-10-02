import type { useTriggerLibraryScan } from "../../lib/processing/library-mode-queries";
import { errorMessage } from "../../lib/api/error-message";
import type {
  LibraryModeSchedule,
  LibraryScanInfo,
} from "../../lib/processing/library-mode-api";
import {
  useAppClockFormatter,
  useAppDateFormatter,
  useAppDayClockFormatter,
} from "../../lib/ui/mm-format-date";
import { nextScheduled, nextScheduledBrief, scanned } from "./library-model";

type Rescan = ReturnType<typeof useTriggerLibraryScan>;

const START_FAILED = "Weir couldn't start a check. Try again in a moment.";

/**
 * When this library's numbers were counted and when the schedule runs next, in a few words with the whole sentence in
 * the tooltip. It sits in the Files card's header, because it is about the files, and says so if a check could not be
 * started.
 */
export function LibraryScanStatus({
  scan,
  schedule,
  now,
  rescan,
}: {
  scan: LibraryScanInfo | null;
  schedule: LibraryModeSchedule | undefined;
  now: number;
  rescan: Rescan;
}) {
  const formatDate = useAppDateFormatter();
  const clock = useAppClockFormatter();
  const dayClock = useAppDayClockFormatter();
  const running = Boolean(scan?.running);
  const when = running
    ? "Checking this workflow now"
    : scanned(scan?.generated_at ?? null, now);
  const scheduleLine = nextScheduled(schedule, now, formatDate);
  const scheduleBrief = nextScheduledBrief(schedule, now, clock, dayClock);
  const sentence = [when, scheduleLine].filter(Boolean).join(" · ");
  return (
    <>
      <span
        className="mm-library-scan"
        data-testid="library-scan"
        title={sentence}
      >
        {running ? (
          <i className="mm-live-pulse" aria-hidden="true" />
        ) : (
          <span className="mm-library-scan__dot" aria-hidden="true" />
        )}
        <span>{when}</span>
        {scheduleBrief ? (
          <span
            className="mm-library-scan__next"
            data-testid="library-schedule"
          >
            · {scheduleBrief}
          </span>
        ) : null}
        <span className="sr-only">
          {scheduleLine ? `, ${scheduleLine}` : ""}
        </span>
      </span>
      {rescan.isError ? (
        <span
          className="mm-library-scan__error"
          role="alert"
          title={errorMessage(rescan.error, START_FAILED)}
          data-testid="library-scan-error"
        >
          {errorMessage(rescan.error, START_FAILED)}
        </span>
      ) : null}
    </>
  );
}

const REFRESH_ICON = (
  <svg
    viewBox="0 0 24 24"
    width="16"
    height="16"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    strokeLinecap="round"
    strokeLinejoin="round"
    aria-hidden="true"
  >
    <path d="M21 12a9 9 0 1 1-2.64-6.36" />
    <path d="M21 4v5h-5" />
  </svg>
);

/**
 * Counts this library again. On the header's title line a header with little room shows only the refresh mark; the
 * words are still its name and its tooltip.
 */
export function LibraryCheckAgain({
  scan,
  rescan,
}: {
  scan: LibraryScanInfo | null;
  rescan: Rescan;
}) {
  const label = rescan.isPending ? "Starting a check…" : "Check again";
  return (
    <button
      type="button"
      className="mm-head-control mm-library-refresh"
      aria-label={label}
      title={label}
      disabled={rescan.isPending || Boolean(scan?.running)}
      onClick={() => rescan.mutate()}
    >
      {REFRESH_ICON}
      <span className="mm-library-refresh__label">{label}</span>
    </button>
  );
}
