/**
 * What happened to one file, told in plain language (#468).
 *
 * The story itself is written by the backend from the stored pass
 * record, so this component only presents it — the raw record stays one click away for anyone who
 * wants the technical detail, but it is never the first thing shown.
 */

import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { DirectPlayLine } from "./direct-play-line";
import { StoryPanelShell } from "./story-panel-shell";
import type {
  ProcessingDirectPlay,
  ProcessingFileLog,
  ProcessingFileStoryStep,
} from "../../lib/processing/files-api";

const TONE_CLASS: Record<string, string> = {
  good: "mm-story-step--good",
  warn: "mm-story-step--warn",
  bad: "mm-story-step--bad",
  neutral: "",
};

function Step({ step }: { step: ProcessingFileStoryStep }): React.ReactElement {
  return (
    <li className={`mm-story-step ${TONE_CLASS[step.tone] ?? ""}`}>
      <p className="mm-story-step__heading">{step.heading}</p>
      <p className="mm-story-step__sentence">{step.sentence}</p>
    </li>
  );
}

export interface FileStoryPanelProps {
  open: boolean;
  fileName: string;
  /** Which of the operator's devices will play the file directly. Information only. */
  directPlay?: ProcessingDirectPlay[];
  log: ProcessingFileLog | undefined;
  loading: boolean;
  error: string | null;
  onClose: () => void;
}

export function FileStoryPanel({
  open,
  fileName,
  directPlay = [],
  log,
  loading,
  error,
  onClose,
}: FileStoryPanelProps): React.ReactElement | null {
  const formatWhen = useAppDateFormatter();

  if (!open) return null;

  const retention =
    log && log.retention_days > 0
      ? `Weir keeps these records for ${log.retention_days} days.`
      : log
        ? "Weir keeps these records until you remove them."
        : null;

  return (
    <StoryPanelShell
      eyebrow="What happened to this file"
      title={fileName}
      backdropLabel="Close file history"
      onClose={onClose}
    >
      <DirectPlayLine
        directPlay={directPlay}
        full
        testId="file-story-direct-play"
      />
      {loading ? (
        <p className="mm-story-panel__note">Reading the record…</p>
      ) : error ? (
        <p className="mm-story-panel__note mm-status-text--failed" role="alert">
          {error}
        </p>
      ) : !log || log.entries.length === 0 ? (
        <p className="mm-story-panel__note">
          Weir has not worked on this file yet, so there is nothing to tell. Its
          story starts the first time it is processed.
        </p>
      ) : (
        log.entries.map((entry) => (
          <section key={entry.id} className="mm-story-pass">
            <h3 className="mm-story-pass__when">
              {formatWhen(entry.recorded_at)}
            </h3>
            {entry.story.length > 0 ? (
              <ol className="mm-story-steps">
                {entry.story.map((step, index) => (
                  <Step key={`${entry.id}-${index}`} step={step} />
                ))}
              </ol>
            ) : (
              <p className="mm-story-panel__note">{entry.title}</p>
            )}
            <details className="mm-story-pass__detail">
              <summary>Show the technical detail</summary>
              <pre>{JSON.stringify(entry.detail, null, 2)}</pre>
            </details>
          </section>
        ))
      )}
      {retention ? (
        <p className="mm-story-panel__retention">{retention}</p>
      ) : null}
    </StoryPanelShell>
  );
}
