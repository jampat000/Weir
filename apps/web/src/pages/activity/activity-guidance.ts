import type { ProcessingFile } from "../../lib/processing/files-api";

/** A file checked while processing was paused carries that reason until something looks at it again. */
function pausedWhenChecked(file: ProcessingFile): boolean {
  return (
    file.status === "out_of_schedule" &&
    file.status_reason.toLowerCase().includes("processing is paused")
  );
}

type Guidance = { title: string; next: string };

const NO_GUIDANCE: Guidance = { title: "", next: "" };

const BY_STATUS: Partial<Record<ProcessingFile["status"], Guidance>> = {
  unprocessed: {
    title: "Ready to process.",
    next: "Start it now or move it to the front of the queue.",
  },
  processing_failed: {
    title: "Weir gave up on this file.",
    next: "Fix the reason and use Try again, or use Pass through unchanged when this is an intentional edge case you want delivered without your rules.",
  },
  skipped: {
    title: "This file does not match the workflow rules.",
    next: "Change the named workflow rule and use Check again, or pass this one file through unchanged when it is a legitimate exception.",
  },
  on_hold: {
    title: "Waiting for the file to settle.",
    next: "Finish the copy or import, then use Check again. Weir will not touch a changing file.",
  },
  blocked_upstream: {
    title: "The media manager still has this file.",
    next: "Use Why is this held? for the manager's answer right now, or Check again after the import finishes.",
  },
  out_of_schedule: {
    title: "This workflow is outside its hours.",
    next: "It is picked up when its hours start; use Check again if you changed them.",
  },
  disabled: {
    title: "This workflow is switched off.",
    next: "Turn the workflow on in Setup › Workflows before processing its files.",
  },
  cancelled: {
    title: "Its queued work was cancelled.",
    next: "The original is untouched. Use Process again when you want Weir to work on it.",
  },
};

/** A failed file with a retry still owed: Weir looks again by itself once the wait is over. */
const RETRY_OWED: Guidance = {
  title: "This attempt failed, and Weir will try again.",
  next: "It looks again shortly after the wait ends. Use Try again to run it now.",
};

/** What a person can do about a file, in the words of the buttons beside it. */
export function fileGuidance(
  file: ProcessingFile,
  processingPaused: boolean,
): Guidance {
  if (pausedWhenChecked(file)) {
    return processingPaused
      ? {
          title: "Processing is paused.",
          next: "Resume it at the top of the page when you want queued work to continue. Check again is only needed after changing this file or its workflow.",
        }
      : {
          title: "Refresh this file's status.",
          next: "Use Check again. Weir will apply the current schedule, readiness, size, and path rules without deleting the original file.",
        };
  }
  if (file.status === "processing_failed" && file.next_retry_at) {
    return RETRY_OWED;
  }
  return BY_STATUS[file.status] ?? NO_GUIDANCE;
}
