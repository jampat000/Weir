/**
 * What happened to one file, told in plain language (#468).
 *
 * The story itself is written by the backend from the stored pass
 * record, so this component only presents it — the raw record stays one click away for anyone who
 * wants the technical detail, but it is never the first thing shown.
 */

import { Poster } from "../shared/poster";
import { fileHistoryRetentionNote } from "../../lib/processing/file-history-retention";
import { useAppDateFormatter } from "../../lib/ui/mm-format-date";
import { DirectPlayLine } from "./direct-play-line";
import { FileNowSection, type FileNow } from "./file-now-section";
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
  /** What the header shows beside the name: the title's poster, tinted for its workflow when there is none. */
  poster?: { url: string | null | undefined; workflow: string };
  /** Which of the operator's devices will play the file directly. Information only. */
  directPlay?: ProcessingDirectPlay[];
  /** Where the file is on the Pipeline right now, when it is on it: shown above what has already happened to it. */
  now?: FileNow;
  log: ProcessingFileLog | undefined;
  loading: boolean;
  error: string | null;
  onClose: () => void;
}

export function FileStoryPanel({
  open,
  fileName,
  poster,
  directPlay = [],
  now,
  log,
  loading,
  error,
  onClose,
}: FileStoryPanelProps): React.ReactElement | null {
  const formatWhen = useAppDateFormatter();

  if (!open) return null;

  const retention = log ? fileHistoryRetentionNote(log.retention_days) : null;

  return (
    <StoryPanelShell
      eyebrow="What happened to this file"
      title={fileName}
      art={
        poster ? (
          <Poster
            url={poster.url}
            title={fileName}
            workflow={poster.workflow}
          />
        ) : undefined
      }
      backdropLabel="Close file activity"
      onClose={onClose}
    >
      <DirectPlayLine
        directPlay={directPlay}
        full
        testId="file-story-direct-play"
      />
      {now ? <FileNowSection now={now} /> : null}
      {loading ? (
        <p className="mm-story-panel__note">Loading…</p>
      ) : error ? (
        <p className="mm-story-panel__note mm-status-text--failed" role="alert">
          {error}
        </p>
      ) : !log || log.entries.length === 0 ? (
        <p className="mm-story-panel__note">
          {now
            ? "What Weir kept and removed shows here once it has finished."
            : "Nothing yet. Weir hasn't worked on this file."}
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
              <summary>Technical detail</summary>
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
