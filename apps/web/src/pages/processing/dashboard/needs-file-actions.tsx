import { Link } from "react-router-dom";

import { errorMessage } from "../../../lib/api/error-message";
import type { ProcessingFile } from "../../../lib/processing/files-api";
import {
  useProcessingCheckLibraryAgain,
  useRequeueProcessingFile,
} from "../../../lib/processing/files-queries";
import { activityFilePath } from "../../activity/activity-links";
import { useFileRemoval } from "../../activity/use-file-removal";

export type NeedNotice = { text: string; failed: boolean };

type NeedFileActionsProps = {
  file: ProcessingFile;
  /** Which kind of media the file's workflow holds, for asking that workflow to look again. */
  mediaScope: "movie" | "tv";
  /** Says what an action did. The row may be gone by then, so the panel shows it. */
  onNotice: (notice: NeedNotice) => void;
  /** Opens the file's story. Without it the file is opened in Activity. */
  onOpen?: (file: ProcessingFile) => void;
};

const CHECK_AGAIN_FAILED = "Couldn't check that workflow.";
const CHECK_AGAIN_DONE = "Checking this workflow again.";
const QUEUE_FAILED = "Couldn't queue that file.";
const QUEUED_AGAIN = "Queued again.";

/**
 * A held, skipped or rejected file is decided in Activity: Choose tracks, why it is held, Pass through unchanged and
 * Process now are there, so its row goes straight to it.
 */
function isDecidedInActivity(file: ProcessingFile): boolean {
  return (
    file.status === "on_hold" ||
    file.status === "skipped" ||
    file.status === "rejected"
  );
}

/** A held or skipped file is not tried: its workflow is asked to look again. */
function asksWorkflowAgain(file: ProcessingFile): boolean {
  return file.status === "on_hold" || file.status === "skipped";
}

function retryWords(file: ProcessingFile) {
  if (asksWorkflowAgain(file)) {
    return {
      label: "Check again",
      title:
        "Checks this workflow now and queues the files that are ready, this one included once it can be read or its rule has changed.",
    };
  }
  return file.status === "rejected"
    ? {
        label: "Process again",
        title:
          "Checks this file again with your current rules, from its original in the watched folder.",
      }
    : {
        label: "Try again",
        title:
          "Tries this file again now, ignoring the automatic wait and attempt limit.",
      };
}

function retryLabel(
  idle: string,
  pending: { requeueing: boolean; checking: boolean },
) {
  if (pending.requeueing) return "Queueing…";
  return pending.checking ? "Checking…" : idle;
}

function removeLabel(removal: { checking: boolean; removing: boolean }) {
  if (removal.checking) return "Checking…";
  return removal.removing ? "Removing…" : "Remove…";
}

/**
 * What a person can do about one file that needs them: try it again, remove it (asking first, in Activity's
 * own dialog, what to do with the file), open its story, and for a file that is held, skipped or rejected open it
 * in Activity, where it is decided.
 */
export function NeedFileActions({
  file,
  mediaScope,
  onNotice,
  onOpen,
}: NeedFileActionsProps) {
  const requeue = useRequeueProcessingFile();
  const checkAgain = useProcessingCheckLibraryAgain();
  const removal = useFileRemoval({
    fileId: file.id,
    fileName: file.relative_path,
    onRemoved: (text) => onNotice({ text, failed: false }),
    onProblem: (text) => onNotice({ text, failed: true }),
  });
  const retry = retryWords(file);
  const retrying = requeue.isPending || checkAgain.isPending;
  const busy = retrying || removal.checking || removal.removing;
  const report = (text: string, failed: boolean) => onNotice({ text, failed });
  const retryNow = () => {
    if (asksWorkflowAgain(file)) {
      checkAgain.mutate(
        { media_scope: mediaScope, library_id: file.library_id },
        {
          onSuccess: () => report(CHECK_AGAIN_DONE, false),
          onError: (error) =>
            report(errorMessage(error, CHECK_AGAIN_FAILED), true),
        },
      );
      return;
    }
    requeue.mutate(file.id, {
      onSuccess: (result) =>
        report(result.requeued > 0 ? QUEUED_AGAIN : result.detail, false),
      onError: (error) => report(errorMessage(error, QUEUE_FAILED), true),
    });
  };
  return (
    <>
      <button
        type="button"
        className="mm-need__button"
        title={retry.title}
        disabled={busy}
        onClick={retryNow}
      >
        {retryLabel(retry.label, {
          requeueing: requeue.isPending,
          checking: checkAgain.isPending,
        })}
      </button>
      <button
        type="button"
        className="mm-need__button"
        title="Asks what to do with the file, then takes it off this list."
        disabled={busy}
        onClick={() => void removal.start()}
      >
        {removeLabel(removal)}
      </button>
      {onOpen ? (
        <button
          type="button"
          className="mm-need__link"
          onClick={() => onOpen(file)}
        >
          Open →
        </button>
      ) : null}
      {onOpen && !isDecidedInActivity(file) ? null : (
        <Link className="mm-need__link" to={activityFilePath(file)}>
          Open in Activity →
        </Link>
      )}
      {removal.dialog}
    </>
  );
}
