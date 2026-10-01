/**
 * Everything Weir is doing, on one page: today's figures, what is being worked on and what comes next, the
 * Pipeline every file moves through, what just finished and what Weir just did, and down the side its health
 * and what needs a person. Every number comes from the server; the page follows the Activity stream rather
 * than polling, and ticks once a second so countdowns and "min ago" move between updates. A working file's
 * percent, ETA and message come from the same stream's live-progress frame (#750), which moves about once a
 * second even though the file list itself only changes on a database write: the start of a pass, a stage
 * change, or its end.
 */
import { useCallback, useState } from "react";
import { useNavigate } from "react-router-dom";

import { SegmentedControl } from "../../components/panels/segmented-control";
import { FileStoryPanel } from "../../components/processing/file-story-panel";
import { ApiEntryError } from "../../components/shared/api-entry-error";
import { PageLoading } from "../../components/shared/page-loading";
import { PageHeader } from "../../components/shell/page-header";
import { ShellHeaderSlot } from "../../components/shell/shell-header-context";
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { activityKeys } from "../../lib/activity/query-keys";
import { useActivityStreamInvalidations } from "../../lib/activity/use-activity-stream-invalidation";
import { loadErrorMessage } from "../../lib/api/error-message";
import { formatBytes } from "../../lib/format/bytes";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import type { ProcessingFile } from "../../lib/processing/files-api";
import { useProcessingFileLog } from "../../lib/processing/files-queries";
import {
  useProcessingFilesAtOnceQuery,
  useProcessingOverviewStatsQuery,
} from "../../lib/processing/queries";
import { processingKeys } from "../../lib/processing/query-keys";
import { useNow } from "../../lib/ui/use-now";
import { ActivityStream } from "./dashboard/activity-stream";
import { HealthPanel } from "./dashboard/health-panel";
import { NeedsPanel } from "./dashboard/needs-panel";
import { FAILED_JOBS_LIMIT } from "./dashboard/needs-model";
import { NextTile } from "./dashboard/next-tile";
import { TodayTile } from "./dashboard/today-tile";
import { useNextItems } from "./dashboard/use-next-items";
import { WorkingTile } from "./dashboard/working-tile";
import { sharedWaitSeconds } from "./dashboard/working-words";
import { useLeavingCards } from "./leaving-cards";
import { JustFinishedShelf } from "./pipeline/just-finished-shelf";
import { PipelineBoard } from "./pipeline/pipeline-board";
import { FILTER_OPTIONS, TODAY_DAYS, type Filter } from "./processing-filter";
import { prettyName } from "./processing-model";
import {
  FILES_QUERY,
  useLanes,
  useRefetchOverdueLooks,
} from "./use-processing-lanes";
import { useFinishedFiles } from "./use-finished-files";
import { ACTIVE_JOBS_LIMIT, WORKING_FILES_QUERY } from "./working-count";

const EYEBROW = "Cleans new downloads and your library";
const NO_FILES: ProcessingFile[] = [];
/** Once a second, so countdowns and "min ago" move between server updates. */
const TICK_MS = 1000;

// A running pass rewrites its progress row several times a second and every write reaches the
// stream, so the lanes follow it closely and the totals, which only change when a file finishes,
// follow it at a gentler pace. Just finished is read from the Activity entry a pass writes as it ends, so it
// refreshes with the lanes: a file leaves them and lands there in the same step (#852).
const LANE_KEYS = [
  processingKeys.fileList(FILES_QUERY),
  processingKeys.fileList(WORKING_FILES_QUERY),
  processingKeys.jobsInspectionList("active", ACTIVE_JOBS_LIMIT),
  activityKeys.recent,
] as const;
const TOTAL_KEYS = [
  processingKeys.overviewStats(TODAY_DAYS),
  processingKeys.filesAtOnce,
  processingKeys.jobsInspectionList("failed", FAILED_JOBS_LIMIT, true),
] as const;
const LANE_THROTTLE_MS = 750;
const TOTAL_THROTTLE_MS = 3_000;

/** "38 cleaned today · 41.2 GB saved", for the shelf's title row. */
function cleanedToday(
  stats: { files_processed: number; net_space_saved_bytes: number } | undefined,
): string | undefined {
  if (!stats) return undefined;
  const saved = formatBytes(stats.net_space_saved_bytes) || "0 B";
  return `${stats.files_processed.toLocaleString()} cleaned today · ${saved} saved`;
}
export function ProcessingPage(): React.ReactElement {
  useActivityStreamInvalidations(LANE_KEYS, { throttleMs: LANE_THROTTLE_MS });
  useActivityStreamInvalidations(TOTAL_KEYS, {
    throttleMs: TOTAL_THROTTLE_MS,
  });
  const now = useNow(TICK_MS);
  const board = useLanes();
  useRefetchOverdueLooks(board, now);
  const { files, libraries, lanes } = board;
  const filesAtOnce = useProcessingFilesAtOnceQuery();
  const stats = useProcessingOverviewStatsQuery(TODAY_DAYS);
  const pause = usePauseQuery();
  const fileLog = useProcessingFileLog();
  const navigate = useNavigate();
  const [storyFile, setStoryFile] = useState<{
    id: number;
    name: string;
  } | null>(null);
  const [filter, setFilter] = useState<Filter>("all");
  // From the unfiltered lanes, so narrowing the page to one kind of file is never taken for a file leaving.
  const finished = useFinishedFiles();
  const leaving = useLeavingCards(
    lanes.waiting,
    lanes.working,
    lanes.handing,
    files.data?.files ?? NO_FILES,
  );
  const next = useNextItems(libraries.data);

  const openFile = useCallback(
    (file: ProcessingFile) => {
      setStoryFile({ id: file.id, name: file.relative_path });
      fileLog.mutate(file.id);
    },
    [fileLog],
  );
  const openFinished = useCallback(
    (item: FinishedFile) => {
      const match = (files.data?.files ?? []).find(
        (f) =>
          f.relative_path === item.relativePath &&
          (item.libraryId == null || f.library_id === item.libraryId),
      );
      if (match) {
        openFile(match);
        return;
      }
      // Older than the files list reaches, or a library file: open its history instead.
      const path = encodeURIComponent(item.relativePath);
      void navigate(
        item.source === "library"
          ? `/library?path=${path}`
          : `/history?q=${path}`,
      );
    },
    [files.data, navigate, openFile],
  );

  if (files.isPending) return <PageLoading label="Loading Processing" />;
  if (files.isError) {
    return (
      <div className="mm-page">
        <ApiEntryError error={files.error} />
      </div>
    );
  }

  const workflowNames = new Map(
    (libraries.data ?? []).map((library) => [library.id, library.name]),
  );
  const paused = pause.data?.paused ?? false;
  const rejectedCount = files.data.status_counts.rejected ?? 0;

  return (
    <div className="mm-page mm-dash" data-testid="processing-page">
      <PageHeader eyebrow={EYEBROW} />
      <ShellHeaderSlot>
        <SegmentedControl
          options={FILTER_OPTIONS}
          value={filter}
          onChange={setFilter}
          ariaLabel="Show work from"
          dataTestId="live-filter"
        />
      </ShellHeaderSlot>

      <div className="mm-dash__grid">
        <div className="mm-dash__band">
          <div className="mm-dash__tiles">
            <TodayTile filter={filter} now={now} />
            <WorkingTile
              working={lanes.working}
              filesAtOnce={filesAtOnce.data?.effective_files_at_once ?? null}
              waitSeconds={sharedWaitSeconds(libraries.data ?? [])}
              onOpen={openFile}
            />
            <NextTile items={next} now={now} paused={paused} />
          </div>
        </div>
        <div className="mm-dash__board">
          <PipelineBoard
            lanes={lanes}
            leaving={leaving}
            filter={filter}
            now={now}
            paused={paused}
            onOpen={openFile}
          />
        </div>
        <div className="mm-dash__low">
          <JustFinishedShelf
            items={finished}
            filter={filter}
            now={now}
            workflowNames={workflowNames}
            count={cleanedToday(stats.data)}
            onOpen={openFinished}
          />
          <ActivityStream now={now} />
        </div>
        <div className="mm-dash__needs">
          <NeedsPanel
            workflows={libraries.data}
            stuck={lanes.stuck}
            rejectedCount={rejectedCount}
          />
        </div>
        <div className="mm-dash__health">
          <HealthPanel workflows={libraries.data ?? []} />
        </div>
      </div>

      <FileStoryPanel
        open={storyFile !== null}
        fileName={storyFile ? prettyName(storyFile.name) : ""}
        log={fileLog.data}
        loading={fileLog.isPending}
        error={
          fileLog.isError
            ? loadErrorMessage(fileLog.error, "what happened to this file")
            : null
        }
        onClose={() => setStoryFile(null)}
      />
    </div>
  );
}
