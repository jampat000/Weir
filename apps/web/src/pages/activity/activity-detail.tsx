import { Link } from "react-router-dom";

import { StatusDot } from "../../components/panels/status-dot";
import { DirectPlayLine } from "../../components/processing/direct-play-line";
import { LoadError } from "../../components/shared/load-error";
import { PanelLoading } from "../../components/shared/page-loading";
import { baseName } from "../../lib/format/path";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import {
  processingFileLogDownloadPath,
  processingFileLead,
  type ProcessingFile,
  type ProcessingFileLogEntry,
} from "../../lib/processing/files-api";
import { fileActivityRetentionNote } from "../../lib/processing/file-activity-retention";
import { useProcessingFileLogQuery } from "../../lib/processing/files-queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import type { WorkflowKind } from "../../lib/processing/workflow-kind";
import { activityGroupOf } from "./activity-entries";
import { ActivityFileActions } from "./activity-file-actions";
import { SizeFigures, WorkingFigures } from "./activity-figures";
import { fileGuidance } from "./activity-guidance";
import {
  detailSizes,
  handbackStory,
  latestPass,
  tookWords,
  workflowKindLookup,
} from "./activity-model";
import { PassRecord } from "./activity-pass";
import { useNarrowDetailFocus } from "./use-narrow-detail-focus";

/** The kind of workflow the file belongs to; null until the workflows are known, so nothing is said wrongly meanwhile. */
function useWorkflowKindOf(file: ProcessingFile): WorkflowKind["kind"] | null {
  const libraries = useProcessingLibrariesQuery();
  return workflowKindLookup(libraries.data)(file);
}

function Guidance({ file, now }: { file: ProcessingFile; now: number }) {
  const pause = usePauseQuery();
  const workflowKind = useWorkflowKindOf(file);
  const guidance = fileGuidance(file, pause.data?.paused === true);
  const handedBack = workflowKind
    ? handbackStory(file.handback, now, workflowKind)
    : null;
  return (
    <>
      {guidance.title ? (
        <div className="mm-history-next">
          <p className="mm-history-next__title">{guidance.title}</p>
          <p className="mm-history-note">{guidance.next}</p>
        </div>
      ) : null}
      {handedBack ? (
        <div className="mm-history-next" data-testid="activity-handback">
          <p className="mm-history-next__title">
            <StatusDot meaning={handedBack.meaning} />
            {handedBack.heading}
          </p>
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
        {file.status === "processing" || activityGroupOf(file) === "working"
          ? "Weir has not finished processing this file yet. What it kept and removed shows here once it has."
          : "Weir has no record of what it did to this file. Records older than the File activity setting are removed."}
      </p>
    );
  }
  return <PassRecord pass={pass} now={now} />;
}

/** One file in full: what happened to it, what to do next, and what the last pass kept and removed. */
export function ActivityDetail({
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
      aria-labelledby="activity-detail-title"
      data-testid="activity-detail"
    >
      <p className="mm-history-detail__eyebrow">{file.library_name}</p>
      <h2
        id="activity-detail-title"
        ref={titleRef}
        tabIndex={-1}
        className="mm-history-detail__title"
      >
        {baseName(file.relative_path)}
      </h2>
      <p className="mm-history-detail__lead">
        {processingFileLead(file)}
        {took && !working ? ` Took ${took}.` : ""}
      </p>

      <Guidance file={file} now={now} />
      <DirectPlayLine
        directPlay={file.direct_play}
        testId="activity-direct-play"
      />
      <ActivityFileActions
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
        {working ? <Link to="/">See it on the Dashboard →</Link> : null}
        <a href={processingFileLogDownloadPath(file.id)}>
          Download its record →
        </a>
      </div>
      {record.data ? (
        <p
          className="mm-history-record-meta"
          data-testid="activity-retention-note"
        >
          {fileActivityRetentionNote(record.data.retention_days)}
        </p>
      ) : null}
    </section>
  );
}
