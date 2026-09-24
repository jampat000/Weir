import { Link } from "react-router-dom";

import { DirectPlayLine } from "../../components/processing/direct-play-line";
import { LoadError } from "../../components/shared/load-error";
import { PanelLoading } from "../../components/shared/page-loading";
import { baseName } from "../../lib/format/path";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import {
  PROCESSING_FILE_STATUS_LABELS,
  processingFileLogDownloadPath,
  type ProcessingFile,
  type ProcessingFileLogEntry,
} from "../../lib/processing/files-api";
import { useProcessingFileLogQuery } from "../../lib/processing/files-queries";
import { historyGroupOf } from "./history-entries";
import { HistoryFileActions } from "./history-file-actions";
import { SizeFigures, WorkingFigures } from "./history-figures";
import { fileGuidance } from "./history-guidance";
import {
  detailSizes,
  handbackStory,
  latestPass,
  tookWords,
} from "./history-model";
import { PassRecord } from "./history-pass";
import { useNarrowDetailFocus } from "./use-narrow-detail-focus";

function Guidance({ file, now }: { file: ProcessingFile; now: number }) {
  const pause = usePauseQuery();
  const guidance = fileGuidance(file, pause.data?.paused === true);
  const handedBack = handbackStory(file.handback, now);
  return (
    <>
      {guidance.title ? (
        <div className="mm-history-next">
          <p className="mm-history-next__title">{guidance.title}</p>
          <p className="mm-history-note">{guidance.next}</p>
        </div>
      ) : null}
      {handedBack ? (
        <div
          className={`mm-history-next mm-history-handback is-${handedBack.tone}`}
          data-testid="history-handback"
        >
          <p className="mm-history-next__title">{handedBack.heading}</p>
          <p className="mm-history-note">{handedBack.sentence}</p>
        </div>
      ) : null}
    </>
  );
}

function RecordSection({
  file,
  pass,
  loading,
  error,
  now,
}: {
  file: ProcessingFile;
  pass: ProcessingFileLogEntry | null;
  loading: boolean;
  error: unknown;
  now: number;
}) {
  if (loading) {
    return <PanelLoading label="Reading this file's record…" />;
  }
  if (error) {
    return <LoadError thing="this file's record" error={error} />;
  }
  if (!pass) {
    return (
      <p className="mm-history-note">
        {file.status === "processing" || historyGroupOf(file) === "working"
          ? "Weir has not finished processing this file yet. What it kept and removed shows here once it has."
          : "Weir has no record of what it did to this file. Records older than the History setting are removed."}
      </p>
    );
  }
  return <PassRecord pass={pass} now={now} />;
}

/** One file in full: what happened to it, what to do next, and what the last pass kept and removed. */
export function HistoryDetail({
  file,
  now,
  editable,
  onRemoved,
}: {
  file: ProcessingFile;
  now: number;
  editable: boolean;
  onRemoved: (message: string) => void;
}) {
  const working = file.status === "processing";
  const record = useProcessingFileLogQuery(file.id, file.updated_at);
  const pass = latestPass(record.data?.entries ?? []);
  const took = pass
    ? tookWords(pass.detail.elapsed_seconds as number | undefined)
    : null;
  const { sectionRef, titleRef } = useNarrowDetailFocus(file.id);

  return (
    <section
      ref={sectionRef}
      className="mm-history-detail"
      aria-labelledby="history-detail-title"
      data-testid="history-detail"
    >
      <p className="mm-history-detail__eyebrow">{file.library_name}</p>
      <h2
        id="history-detail-title"
        ref={titleRef}
        tabIndex={-1}
        className="mm-history-detail__title"
      >
        {baseName(file.relative_path)}
      </h2>
      <p className="mm-history-detail__lead">
        {PROCESSING_FILE_STATUS_LABELS[file.status] ?? file.status}
        {file.status_reason ? `. ${file.status_reason}` : ""}
        {took && !working ? ` Took ${took}.` : ""}
      </p>

      <Guidance file={file} now={now} />
      <DirectPlayLine
        directPlay={file.direct_play}
        testId="history-direct-play"
      />
      <HistoryFileActions
        key={file.id}
        file={file}
        editable={editable}
        onRemoved={onRemoved}
      />

      {working ? (
        <WorkingFigures file={file} />
      ) : (
        <SizeFigures sizes={detailSizes(file, pass?.detail ?? null)} />
      )}

      <RecordSection
        file={file}
        pass={pass}
        loading={record.isLoading}
        error={record.error}
        now={now}
      />

      <div className="mm-history-links">
        {working ? <Link to="/">See it on Processing →</Link> : null}
        <a href={processingFileLogDownloadPath(file.id)}>
          Download its record →
        </a>
      </div>
    </section>
  );
}
