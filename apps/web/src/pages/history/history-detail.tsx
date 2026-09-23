import { Link } from "react-router-dom";

import { DirectPlayLine } from "../../components/processing/direct-play-line";
import { baseName } from "../../lib/format/path";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import {
  PROCESSING_FILE_STATUS_LABELS,
  processingFileLogDownloadPath,
  type ProcessingFile,
  type ProcessingFileLogEntry,
} from "../../lib/processing/files-api";
import { useProcessingFileLogQuery } from "../../lib/processing/files-queries";
import { HistoryFileActions, fileGuidance } from "./history-file-actions";
import { SizeFigures, WorkingFigures } from "./history-figures";
import {
  detailSizes,
  handbackStory,
  historyGroupOf,
  latestPass,
  tookWords,
} from "./history-model";
import { PassRecord } from "./history-pass";

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
  failed,
  now,
}: {
  file: ProcessingFile;
  pass: ProcessingFileLogEntry | null;
  loading: boolean;
  failed: boolean;
  now: number;
}) {
  if (loading) {
    return <p className="mm-history-note">Reading this file&rsquo;s record…</p>;
  }
  if (failed) {
    return (
      <p className="mm-history-note" role="alert">
        Weir could not read this file&rsquo;s record.
      </p>
    );
  }
  if (!pass) {
    return (
      <p className="mm-history-note">
        {file.status === "processing" || historyGroupOf(file) === "working"
          ? "Weir has not finished a pass over this file yet. What it kept and removed shows here once it has."
          : "Weir kept no record of a pass over this file. Records older than the History setting are removed."}
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
}: {
  file: ProcessingFile;
  now: number;
  editable: boolean;
}) {
  const working = file.status === "processing";
  const record = useProcessingFileLogQuery(file.id, file.updated_at);
  const pass = latestPass(record.data?.entries ?? []);
  const took = pass
    ? tookWords(pass.detail.elapsed_seconds as number | undefined)
    : null;

  return (
    <section
      className="mm-history-detail"
      aria-labelledby="history-detail-title"
      data-testid="history-detail"
    >
      <p className="mm-history-detail__eyebrow">{file.library_name}</p>
      <h2 id="history-detail-title" className="mm-history-detail__title">
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
      <HistoryFileActions key={file.id} file={file} editable={editable} />

      {working ? (
        <WorkingFigures file={file} />
      ) : (
        <SizeFigures sizes={detailSizes(file, pass?.detail ?? null)} />
      )}

      <RecordSection
        file={file}
        pass={pass}
        loading={record.isLoading}
        failed={record.isError}
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
