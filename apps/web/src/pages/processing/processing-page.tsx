/**
 * Every file Weir is working on, from the moment it lands to the moment the media manager has it
 * back, moving without a reload.
 *
 * Five lanes on a wide screen: Arriving, Waiting, Working, Handing back, Just finished. On a laptop the
 * lanes fold into three columns and on a phone into one, by container query in weir-processing.css, so
 * nothing ever scrolls sideways.
 *
 * Every number comes from the server: file states and the running pass's progress, library clean
 * jobs, the files-at-once read-out, today's totals, and the Activity entry written when a file
 * finished. The page follows the Activity stream rather than polling, and ticks once a second so the
 * countdowns and "min ago" labels move between updates.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";

import { FileStoryPanel } from "../../components/processing/file-story-panel";
import { ApiEntryError } from "../../components/shared/api-entry-error";
import { PageLoading } from "../../components/shared/page-loading";
import { PageHeader } from "../../components/shell/page-header";
import type { FinishedFile } from "../../lib/activity/processing-outcome";
import { activityKeys } from "../../lib/activity/query-keys";
import { useActivityStreamInvalidations } from "../../lib/activity/use-activity-stream-invalidation";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import type { ProcessingFile } from "../../lib/processing/files-api";
import {
  useProcessingFileLog,
  useProcessingFilesQuery,
} from "../../lib/processing/files-queries";
import { useProcessingJobsInspectionQuery } from "../../lib/processing/jobs-inspection/queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import { useProcessingFilesAtOnceQuery } from "../../lib/processing/queries";
import { processingKeys } from "../../lib/processing/query-keys";
import { parseAppTime } from "../../lib/ui/mm-format-date";
import { plural } from "../../lib/ui/mm-plural";
import { useNow } from "../../lib/ui/use-now";
import { FinishedLane } from "./finished-lane";
import { EmptyLane, Lane, More } from "./lane";
import { ArrivingCard, HandingCard, WaitingCard } from "./lane-cards";
import { arrivingDeadline, buildLanes, prettyName } from "./processing-model";
import { FAILED_JOBS_LIMIT, NeedsList } from "./processing-needs";
import {
  ProcessingToolbar,
  TODAY_DAYS,
  type Filter,
} from "./processing-toolbar";
import { WorkingCard } from "./working-card";

const FILES_QUERY = { limit: 200 } as const;
const ACTIVE_JOBS_LIMIT = 50;
/** Once a second, so countdowns and "min ago" move between server updates. */
const TICK_MS = 1000;
/** Arriving counts down to each library's next look, which moves with every scan. */
const LIBRARIES_REFRESH_MS = 10_000;
/** How long past a countdown's end before Weir's answer is fetched, and how often at most. */
const LOOK_OVERDUE_MS = 1500;
const LOOK_REFETCH_GAP_MS = 3000;
// How many cards a lane shows before it says how many more there are. Working is never cut short:
// it holds at most as many files as the files-at-once setting allows, and that tops out at 10.
const ARRIVING_SHOWN = 3;
const WAITING_SHOWN = 5;
const HANDING_SHOWN = 4;

// A running pass rewrites its progress row several times a second and every write reaches the
// stream, so the lanes follow it closely and the totals, which only change when a file finishes,
// follow it at a gentler pace.
const LANE_KEYS = [
  processingKeys.fileList(FILES_QUERY),
  processingKeys.jobsInspectionList("active", ACTIVE_JOBS_LIMIT),
] as const;
const TOTAL_KEYS = [
  processingKeys.overviewStats(TODAY_DAYS),
  processingKeys.filesAtOnce,
  processingKeys.jobsInspectionList("failed", FAILED_JOBS_LIMIT),
  activityKeys.recent,
] as const;
const LANE_THROTTLE_MS = 750;
const TOTAL_THROTTLE_MS = 3_000;

/** The lanes' files, grouped by where each one is, with what each library knows about its next look. */
function useLanes() {
  const files = useProcessingFilesQuery(FILES_QUERY);
  const libraries = useProcessingLibrariesQuery(true, LIBRARIES_REFRESH_MS);
  const activeJobs = useProcessingJobsInspectionQuery(
    "active",
    ACTIVE_JOBS_LIMIT,
  );
  const lanes = useMemo(() => {
    const all = libraries.data ?? [];
    const nextLooks = new Map<number, { at: number; interval: number }>();
    for (const library of all) {
      const at = parseAppTime(library.next_look_at);
      if (at != null) {
        nextLooks.set(library.id, {
          at,
          interval: library.scan_interval_seconds,
        });
      }
    }
    return buildLanes(
      files.data?.files ?? [],
      activeJobs.data?.jobs ?? [],
      new Map(all.map((l) => [l.id, l.name])),
      new Map(all.map((l) => [l.id, l.min_file_age_seconds])),
      nextLooks,
    );
  }, [files.data, activeJobs.data, libraries.data]);
  return { files, libraries, lanes };
}

/**
 * A countdown that has run out means Weir is looking at that file now. Its answer, picked up or held
 * again for a new reason with a new time, only reaches the screen as fresh data, and a scan that
 * changes nothing else sends no live event, so fetch it rather than keep saying "checking it now".
 */
function useRefetchOverdueLooks(
  { files, libraries, lanes }: ReturnType<typeof useLanes>,
  now: number,
) {
  const lastRefetch = useRef(0);
  useEffect(() => {
    const due = lanes.arriving.some((item) => {
      const deadline = arrivingDeadline(item);
      return deadline != null && deadline <= now - LOOK_OVERDUE_MS;
    });
    if (!due || now - lastRefetch.current < LOOK_REFETCH_GAP_MS) return;
    lastRefetch.current = now;
    void files.refetch();
    void libraries.refetch();
  }, [now, lanes.arriving, files, libraries]);
}

export function ProcessingPage(): React.ReactElement {
  useActivityStreamInvalidations(LANE_KEYS, { throttleMs: LANE_THROTTLE_MS });
  useActivityStreamInvalidations(TOTAL_KEYS, {
    throttleMs: TOTAL_THROTTLE_MS,
  });
  const now = useNow(TICK_MS);
  const board = useLanes();
  useRefetchOverdueLooks(board, now);
  const { files, lanes } = board;
  const filesAtOnce = useProcessingFilesAtOnceQuery();
  const pause = usePauseQuery();
  const fileLog = useProcessingFileLog();
  const navigate = useNavigate();
  const [storyFile, setStoryFile] = useState<{
    id: number;
    name: string;
  } | null>(null);
  const [filter, setFilter] = useState<Filter>("all");

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
          : `/system?tab=history&show=downloads&path=${path}`,
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

  const shows = (item: { source: Filter }) =>
    filter === "all" || filter === item.source;
  const arriving = filter === "library" ? [] : lanes.arriving;
  const waiting = lanes.waiting.filter(shows);
  const working = lanes.working.filter(shows);
  const handing = lanes.handing.filter(shows);
  const lanesAtOnce = filesAtOnce.data?.effective_files_at_once ?? null;

  return (
    <div className="mm-page mm-live" data-testid="processing-page">
      <PageHeader
        title="Processing"
        lead="Every file Weir is working on, from the moment it lands to the moment your media manager has it back."
      />

      <ProcessingToolbar filter={filter} onFilter={setFilter} now={now} />

      {pause.data?.paused ? (
        <p className="mm-live-paused" role="status" data-testid="live-paused">
          <b>Paused.</b> {pause.data.reason} Files already being written finish;
          nothing new starts.
        </p>
      ) : null}

      <NeedsList stuck={lanes.stuck} />

      {/* Arriving and Waiting share a column below five-lane width, as do Handing back and Just
          finished; on a wide screen the two wrappers dissolve and all five sit side by side. */}
      <div className="mm-live-board">
        <div className="mm-live-lanes">
          <div className="mm-live-col mm-live-col--next">
            <Lane
              id="arriving"
              active={arriving.length > 0}
              label="Arriving"
              count={arriving.length}
              hint="Not ready yet: still being written, or held back for a while"
            >
              {arriving.length ? (
                <ul className="mm-live-lane__body">
                  {arriving.slice(0, ARRIVING_SHOWN).map((item) => (
                    <ArrivingCard key={item.key} item={item} now={now} />
                  ))}
                  <More
                    count={arriving.length - ARRIVING_SHOWN}
                    what="arriving"
                  />
                </ul>
              ) : (
                <EmptyLane>
                  Nothing arriving. New downloads show up here within seconds.
                </EmptyLane>
              )}
            </Lane>

            <Lane
              id="waiting"
              active={waiting.length > 0}
              label="Waiting"
              count={waiting.length}
              hint="Ready, queued for the next free lane"
            >
              {waiting.length ? (
                <ul className="mm-live-lane__body">
                  {waiting.slice(0, WAITING_SHOWN).map((item, index) => (
                    <WaitingCard key={item.key} item={item} index={index} />
                  ))}
                  <More count={waiting.length - WAITING_SHOWN} what="waiting" />
                </ul>
              ) : (
                <EmptyLane>Nothing waiting.</EmptyLane>
              )}
            </Lane>
          </div>

          <Lane
            id="working"
            active={working.length > 0}
            label="Working"
            live={working.length > 0}
            count={null}
            hint="Writing a copy that keeps only the tracks your rules want"
            aside={
              <span className="mm-live-lane__count">
                {working.length}
                {lanesAtOnce != null ? (
                  <small> of {plural(lanesAtOnce, "lane", "lanes")}</small>
                ) : null}
              </span>
            }
          >
            {/* The cards measure this, not the lane: a lane that is a size container cannot also
                share the board's rows (subgrid), and sharing them is what lines the lanes up. */}
            <div className="mm-live-work-area">
              {working.length ? (
                <ul className="mm-live-lane__body mm-live-lane__body--work">
                  {working.map((item) => (
                    <WorkingCard key={item.key} item={item} onOpen={openFile} />
                  ))}
                </ul>
              ) : (
                <EmptyLane>
                  {pause.data?.paused
                    ? "Paused. Nothing new starts until you resume."
                    : "A lane is free. The next file starts as soon as it is ready."}
                </EmptyLane>
              )}
            </div>
          </Lane>

          <div className="mm-live-col mm-live-col--done">
            <Lane
              id="handing"
              active={handing.length > 0}
              label="Handing back"
              count={handing.length}
              hint="Final checks, then back to your media manager"
            >
              {handing.length ? (
                <ul className="mm-live-lane__body">
                  {handing.slice(0, HANDING_SHOWN).map((item) => (
                    <HandingCard key={item.key} item={item} />
                  ))}
                  <More
                    count={handing.length - HANDING_SHOWN}
                    what="on final checks"
                  />
                </ul>
              ) : (
                <EmptyLane>Nothing on its final checks.</EmptyLane>
              )}
            </Lane>

            <FinishedLane filter={filter} now={now} onOpen={openFinished} />
          </div>
        </div>
      </div>

      <FileStoryPanel
        open={storyFile !== null}
        fileName={storyFile ? prettyName(storyFile.name) : ""}
        log={fileLog.data}
        loading={fileLog.isPending}
        error={fileLog.isError ? fileLog.error.message : null}
        onClose={() => setStoryFile(null)}
      />
    </div>
  );
}
