import { useState } from "react";
import { ChooseTracksPanel } from "../../components/processing/choose-tracks-panel";
import { ConfirmDialog } from "../../components/ui/confirm-dialog";
import type {
  ProcessingFile,
  ProcessingFileTracks,
  ProcessingManualPlanChoice,
} from "../../lib/processing/files-api";
import {
  useForgetProcessingFile,
  useMoveProcessingFileToTop,
  useProcessProcessingFileNow,
  useProcessingCheckLibraryAgain,
  useProcessingFileTracks,
  useProcessingWhyHeld,
  useRequeueProcessingFile,
  useSubmitProcessingManualPlan,
} from "../../lib/processing/files-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";

const PASS_THROUGH_EXPLAINED =
  "Weir will skip the audio, subtitle and metadata rules, copy and check the original in this library's output folder, then remove the watched original the way it does after any finished file. Readiness checks still apply, so a download still being written is left alone.";

/** A file checked while processing was paused carries that reason until something looks at it again. */
export function pausedWhenChecked(file: ProcessingFile): boolean {
  return (
    file.status === "out_of_schedule" &&
    file.status_reason.toLowerCase().includes("processing is paused")
  );
}

/**
 * What a person can do about a file, in the words of the buttons beside it.
 */
export function fileGuidance(
  file: ProcessingFile,
  processingPaused: boolean,
): { title: string; next: string } {
  if (pausedWhenChecked(file)) {
    return processingPaused
      ? {
          title: "Processing is paused.",
          next: "Resume it at the top of the page when you want queued work to continue. Check again is only needed after changing this file or its library.",
        }
      : {
          title: "Refresh this file's status.",
          next: "Use Check again. Weir will apply the current schedule, readiness, size, and path rules without deleting the original file.",
        };
  }
  switch (file.status) {
    case "unprocessed":
      return {
        title: "Ready to process.",
        next: "Start it now or move it to the front of the queue.",
      };
    case "processing_failed":
      return {
        title: "This attempt failed.",
        next: "Fix the reason and use Try again, or use Pass through unchanged when this is an intentional edge case you want delivered without your rules.",
      };
    case "skipped":
      return {
        title: "This file does not match the library rules.",
        next: "Change the named library rule and use Check again, or pass this one file through unchanged when it is a legitimate exception.",
      };
    case "on_hold":
      return {
        title: "Waiting for the file to settle.",
        next: "Finish the copy or import, then use Check again. Weir will not touch a changing file.",
      };
    case "blocked_upstream":
      return {
        title: "The media manager still has this file.",
        next: "Use Why is this held? for the manager's answer right now, or Check again after the import finishes.",
      };
    case "out_of_schedule":
      return {
        title: "This library is outside its hours.",
        next: "It is picked up when its hours start; use Check again if you changed them.",
      };
    case "disabled":
      return {
        title: "This library is switched off.",
        next: "Turn the library on in Settings › Libraries before processing its files.",
      };
    default:
      return {
        title: "",
        next: "",
      };
  }
}

/**
 * Every action for the file open in History. Each shows only where it can do something: a running file cannot be started earlier, and a button that looked
 * like it worked would be worse than no button.
 */
export function HistoryFileActions({
  file,
  editable,
}: {
  file: ProcessingFile;
  editable: boolean;
}) {
  const libraries = useProcessingLibrariesQuery();
  const forget = useForgetProcessingFile();
  const moveToTop = useMoveProcessingFileToTop();
  const requeue = useRequeueProcessingFile();
  const whyHeld = useProcessingWhyHeld();
  const processNow = useProcessProcessingFileNow();
  const checkAgain = useProcessingCheckLibraryAgain();
  const fileTracks = useProcessingFileTracks();
  const submitManualPlan = useSubmitProcessingManualPlan();
  const [notice, setNotice] = useState<string | null>(null);
  const [tracksOpen, setTracksOpen] = useState(false);
  const [tracks, setTracks] = useState<ProcessingFileTracks | null>(null);
  const [tracksError, setTracksError] = useState<string | null>(null);
  const [planError, setPlanError] = useState<string | null>(null);
  const [confirmingPassThrough, setConfirmingPassThrough] = useState(false);

  if (!editable) return null;

  const scope =
    libraries.data?.find((l) => l.id === file.library_id)?.media_type === "tv"
      ? "tv"
      : "movie";

  async function run(action: () => Promise<string>, failed: string) {
    setNotice(null);
    try {
      setNotice(await action());
    } catch {
      setNotice(failed);
    }
  }

  async function openTracks() {
    setNotice(null);
    setTracksOpen(true);
    setTracks(null);
    setTracksError(null);
    setPlanError(null);
    try {
      setTracks(await fileTracks.mutateAsync(file.id));
    } catch {
      setTracksError(
        "Weir could not read this file's tracks. Refresh and try again.",
      );
    }
  }

  async function submitTracks(choice: ProcessingManualPlanChoice) {
    setPlanError(null);
    try {
      await submitManualPlan.mutateAsync({ id: file.id, choice });
      setNotice(
        "Queued your track choice. Weir will check the file again before running the pass.",
      );
      setTracksOpen(false);
    } catch {
      setPlanError(
        "That track choice could not be queued. Fix the reported problem, or refresh the tracks and try again.",
      );
    }
  }

  const status = file.status;
  const button = (
    label: string,
    title: string,
    onClick: () => void,
    variant: "secondary" | "tertiary" = "secondary",
  ) => (
    <button
      type="button"
      className={mmActionButtonClass({ variant })}
      title={title}
      onClick={onClick}
    >
      {label}
    </button>
  );

  return (
    <div className="mm-history-actions" data-testid="history-file-actions">
      <div className="mm-history-actions__row">
        {status === "processing_failed"
          ? button(
              "Try again",
              "Tries this file again now, ignoring the automatic wait and attempt limit.",
              () =>
                void run(
                  async () => (await requeue.mutateAsync(file.id)).detail,
                  "That file could not be queued again.",
                ),
            )
          : null}
        {status === "unprocessed"
          ? button(
              "Process now",
              "Queues this file straight away.",
              () =>
                void run(async () => {
                  await processNow.mutateAsync({
                    relative_media_path: file.relative_path,
                    media_scope: scope,
                    library_id: file.library_id,
                  });
                  return "Queued this file. It starts as soon as a lane is free.";
                }, "That file could not be queued."),
            )
          : null}
        {status === "unprocessed"
          ? button(
              "Move to top",
              "Puts this file ahead of everything else waiting.",
              () =>
                void run(
                  async () => (await moveToTop.mutateAsync(file.id)).detail,
                  "That file could not be moved to the front of the queue.",
                ),
              "tertiary",
            )
          : null}
        {status === "on_hold"
          ? button(
              "Choose tracks",
              "Reads this file's tracks again and lets you pick which to keep, instead of the saved rules.",
              () => void openTracks(),
            )
          : null}
        {status === "on_hold" ||
        status === "blocked_upstream" ||
        status === "skipped" ||
        status === "out_of_schedule"
          ? button(
              "Check again",
              "Checks this library now and queues files that are ready.",
              () =>
                void run(async () => {
                  await checkAgain.mutateAsync({
                    media_scope: scope,
                    library_id: file.library_id,
                  });
                  return "Weir is checking this library again and will queue the file when it is ready.";
                }, "That library could not be checked again. Review its watched folder and try again."),
            )
          : null}
        {status === "blocked_upstream" || status === "on_hold"
          ? button(
              "Why is this held?",
              "Asks every media manager covering this library what it is doing with this file, right now.",
              () =>
                void run(async () => {
                  const answer = await whyHeld.mutateAsync(file.id);
                  return answer.reasons.length
                    ? answer.reasons.join(" ")
                    : "The media managers had nothing to say about this file.";
                }, "Weir could not ask why that file is held."),
              "tertiary",
            )
          : null}
        {status !== "processing" &&
        status !== "processed" &&
        status !== "disabled"
          ? button(
              "Pass through unchanged",
              "Skips your track rules, places a checked, unchanged copy in the output folder, then tidies the original as after any finished file.",
              () => setConfirmingPassThrough(true),
              "tertiary",
            )
          : null}
        {status !== "processing"
          ? button(
              "Remove from list",
              "Forgets Weir's record of this file. The file on disk is untouched.",
              () =>
                void run(async () => {
                  await forget.mutateAsync(file.id);
                  return "Removed from the list. The file on disk is untouched.";
                }, "That file could not be removed from the list."),
              "tertiary",
            )
          : null}
      </div>
      {confirmingPassThrough ? (
        <ConfirmDialog
          testId="history-pass-through-confirm"
          title="Pass this file through unchanged?"
          description={PASS_THROUGH_EXPLAINED}
          confirmLabel="Pass through unchanged"
          cancelLabel="Not now"
          onCancel={() => setConfirmingPassThrough(false)}
          onConfirm={() => {
            setConfirmingPassThrough(false);
            void run(async () => {
              await processNow.mutateAsync({
                relative_media_path: file.relative_path,
                media_scope: scope,
                library_id: file.library_id,
                pass_through_unchanged: true,
              });
              return "Queued to pass through unchanged. Weir checks the copy before removing the original.";
            }, "That file could not be queued to pass through. Refresh and review its library's folders.");
          }}
        />
      ) : null}
      {notice ? (
        <p className="mm-history-note" role="status">
          {notice}
        </p>
      ) : null}
      <ChooseTracksPanel
        open={tracksOpen}
        fileName={file.relative_path}
        tracks={tracks ?? undefined}
        loading={fileTracks.isPending}
        loadError={tracksError}
        submitting={submitManualPlan.isPending}
        submitError={planError}
        onClose={() => setTracksOpen(false)}
        onSubmit={(choice) => void submitTracks(choice)}
      />
    </div>
  );
}
