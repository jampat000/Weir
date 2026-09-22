/**
 * Live — the first screen since 3.2. Every file Weir is working on, from the moment it lands to the
 * moment the media manager has it back, moving without a reload (docs/exec-plans/active/live-and-library.md,
 * signed off by James on 2026-09-22 against the design canvas).
 *
 * Five lanes on a wide screen: Arriving, Waiting, Working, Handing back, Just finished. On a laptop the
 * lanes fold into three columns and on a phone into one, by container query in weir-processing.css, so
 * nothing ever scrolls sideways. It replaced Home and Processing › Overview, which showed the same
 * status band twice under different words.
 *
 * Every number comes from the server: file states and the running pass's progress, library clean
 * jobs, the files-at-once read-out, today's totals, and the Activity entry written when a file
 * finished. The page follows the Activity stream rather than polling, and ticks once a second so the
 * countdowns and "min ago" labels move between updates.
 */
import { useCallback, useEffect, useMemo, useState } from "react";
import type { CSSProperties } from "react";
import { Link, useNavigate } from "react-router-dom";
import { FileStoryPanel } from "../../components/processing/file-story-panel";
import { PageHeader } from "../../components/shell/page-header";
import { ApiEntryError } from "../../components/shared/api-entry-error";
import { PageLoading } from "../../components/shared/page-loading";
import {
  LIBRARY_FILE_CLEANED_EVENT,
  REMUX_PASS_COMPLETED_EVENT,
  finishedFileFromEvent,
  type FinishedFile,
} from "../../lib/activity/processing-outcome";
import {
  activityRecentKey,
  useActivityRecentQuery,
} from "../../lib/activity/queries";
import { useActivityStreamInvalidations } from "../../lib/activity/use-activity-stream-invalidation";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import type { ProcessingFile } from "../../lib/processing/files-api";
import {
  processingFilesKey,
  useProcessingFileLog,
  useProcessingFilesQuery,
} from "../../lib/processing/files-queries";
import {
  processingJobsInspectionQueryKey,
  useProcessingJobsInspectionQuery,
} from "../../lib/processing/jobs-inspection/queries";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import {
  processingFilesAtOnceQueryKey,
  processingOverviewStatsQueryKey,
  useProcessingFilesAtOnceQuery,
  useProcessingOverviewStatsQuery,
} from "../../lib/processing/queries";
import { useSystemReadinessQuery } from "../../lib/system/readiness-queries";
import { formatBytes } from "../../lib/format/bytes";
import {
  ago,
  buildLanes,
  finishedLine,
  prettyName,
  secondsLeft,
  throughput,
  timeLeft,
  type ArrivingItem,
  type HandingItem,
  type WaitingItem,
  type WorkSource,
  type WorkingItem,
} from "./processing-model";

const FILES_QUERY = { limit: 200 } as const;
const FAILED_JOBS_LIMIT = 100;
const ACTIVE_JOBS_LIMIT = 50;
// How many cards a lane shows before it says how many more there are. Working is never cut short:
// it holds at most as many files as the files-at-once setting allows, and that tops out at 10.
const ARRIVING_SHOWN = 6;
const WAITING_SHOWN = 8;
const HANDING_SHOWN = 6;
const FINISHED_SHOWN = 8;

// A running pass rewrites its progress row several times a second and every write reaches the
// stream, so the lanes follow it closely and the totals, which only change when a file finishes,
// follow it at a gentler pace.
const LANE_KEYS = [
  processingFilesKey(FILES_QUERY),
  processingJobsInspectionQueryKey("active", ACTIVE_JOBS_LIMIT),
] as const;
const TOTAL_KEYS = [
  [...processingOverviewStatsQueryKey, 1],
  processingFilesAtOnceQueryKey,
  processingJobsInspectionQueryKey("failed", FAILED_JOBS_LIMIT),
  activityRecentKey,
] as const;

type Filter = "all" | WorkSource;

/** Re-renders once a second so countdowns and "min ago" move between server updates. */
function useNow(intervalMs = 1000): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = window.setInterval(() => setNow(Date.now()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
  return now;
}

/** 0 → "1st", 10 → "11th", 21 → "22nd". */
function ordinal(index: number): string {
  const n = index + 1;
  const teens = n % 100 >= 11 && n % 100 <= 13;
  return `${n}${teens ? "th" : (["th", "st", "nd", "rd"][n % 10] ?? "th")}`;
}

function SourceTag({
  source,
  libraryName,
}: {
  source: WorkSource;
  libraryName: string;
}) {
  return (
    <span className={`mm-live-tag mm-live-tag--${source}`}>
      {source === "library" ? "Library" : "Download"} · {libraryName}
    </span>
  );
}

function Ring({ item, now }: { item: ArrivingItem; now: number }) {
  const left = secondsLeft(item.holdUntil, now);
  const circumference = 94.2;
  // With a clock on the wait the ring fills as it runs out; without one it turns slowly instead.
  // At zero the wait is over and the file is due on Weir's next look, so it turns rather than sit at 0s.
  const fraction =
    left != null && left > 0 && item.holdTotal
      ? Math.min(1, Math.max(0, 1 - left / item.holdTotal))
      : null;
  return (
    <svg
      className={`mm-live-ring${fraction == null ? " mm-live-ring--waiting" : ""}`}
      width="38"
      height="38"
      viewBox="0 0 38 38"
      aria-hidden="true"
    >
      <circle className="mm-live-ring__track" cx="19" cy="19" r="15" />
      <circle
        className={`mm-live-ring__fill${item.upstream ? " mm-live-ring__fill--upstream" : ""}`}
        cx="19"
        cy="19"
        r="15"
        strokeDasharray={circumference}
        strokeDashoffset={
          fraction == null
            ? circumference * 0.72
            : circumference * (1 - fraction)
        }
      />
      <text x="19" y="23" textAnchor="middle" className="mm-live-ring__text">
        {left != null && left > 0 ? `${left}s` : "…"}
      </text>
    </svg>
  );
}

function ArrivingCard({ item, now }: { item: ArrivingItem; now: number }) {
  const left = secondsLeft(item.holdUntil, now);
  return (
    <li className="mm-live-card" data-testid="live-arriving">
      <div className="mm-live-card__row">
        <Ring item={item} now={now} />
        <div className="mm-live-card__names">
          <span className="mm-live-card__title">{item.name}</span>
          <span className="mm-live-card__sub">{item.facts}</span>
        </div>
      </div>
      <p className="mm-live-card__note">
        {left === 0 && !item.upstream
          ? "Due now. Weir picks it up on its next look."
          : item.note}
      </p>
    </li>
  );
}

function WaitingCard({ item, index }: { item: WaitingItem; index: number }) {
  return (
    <li className="mm-live-card" data-testid="live-waiting">
      <div className="mm-live-card__meta">
        <span className="mm-live-card__sub">{ordinal(index)} in line</span>
        <SourceTag source={item.source} libraryName={item.libraryName} />
      </div>
      <span className="mm-live-card__title">{item.name}</span>
      <span className="mm-live-card__sub">{item.note ?? item.facts}</span>
    </li>
  );
}

const STEPS = ["Checked", "Planned", "Writing", "Verify", "Hand back"];

function WorkingCard({
  item,
  onOpen,
}: {
  item: WorkingItem;
  onOpen: (file: ProcessingFile) => void;
}) {
  const writing = item.percent != null;
  const removed: string[] = [];
  if (item.removedAudio) removed.push(`${item.removedAudio} audio`);
  if (item.removedSubtitles)
    removed.push(
      `${item.removedSubtitles} ${item.removedSubtitles === 1 ? "subtitle" : "subtitles"}`,
    );
  const file = item.file;
  const title = file ? (
    <button
      type="button"
      className="mm-live-card__open"
      onClick={() => onOpen(file)}
    >
      {item.name}
    </button>
  ) : (
    item.name
  );
  return (
    <li className="mm-live-card mm-live-card--work" data-testid="live-working">
      <div className="mm-live-work__top">
        <div className="mm-live-card__names">
          <SourceTag source={item.source} libraryName={item.libraryName} />
          <span className="mm-live-card__title mm-live-card__title--lg">
            {title}
          </span>
          <span className="mm-live-card__sub">{item.facts}</span>
        </div>
        <div className="mm-live-work__pct">
          <span className="mm-live-work__number">
            {writing ? `${Math.floor(item.percent ?? 0)}%` : ""}
          </span>
          <span className="mm-live-card__sub">
            {writing
              ? timeLeft(item.etaSeconds)
              : item.source === "library"
                ? "Cleaning in place"
                : "Checking the file"}
          </span>
        </div>
      </div>
      <div
        className={`mm-live-bar${writing ? "" : " mm-live-bar--busy"}`}
        role="progressbar"
        aria-label={`Progress for ${item.name}`}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={writing ? Math.round(item.percent ?? 0) : undefined}
      >
        <span
          className="mm-live-bar__fill"
          style={
            {
              "--mm-live-fill": `${Math.max(2, item.percent ?? 0)}%`,
            } as CSSProperties
          }
        />
      </div>
      {item.source === "download" ? (
        <ol className="mm-live-steps" aria-label="Steps">
          {STEPS.map((step, index) => {
            const state = !writing
              ? index === 0
                ? "now"
                : "next"
              : index < 2
                ? "done"
                : index === 2
                  ? "now"
                  : "next";
            return (
              <li key={step} className={`mm-live-step mm-live-step--${state}`}>
                {state === "now" && !writing ? "Checking" : step}
                {state === "done" ? " ✓" : ""}
              </li>
            );
          })}
        </ol>
      ) : null}
      {removed.length || item.speed ? (
        <p className="mm-live-work__facts">
          {removed.length ? (
            <span>
              Removing <b>{removed.join(", ")}</b>
            </span>
          ) : null}
          {item.speed ? (
            <span>{item.speed.replace(/x$/i, "×")} real time</span>
          ) : null}
        </p>
      ) : null}
    </li>
  );
}

function HandingCard({ item }: { item: HandingItem }) {
  return (
    <li className="mm-live-card" data-testid="live-handing">
      <SourceTag source={item.source} libraryName={item.libraryName} />
      <span className="mm-live-card__title">{item.name}</span>
      <p className="mm-live-card__note mm-live-card__note--busy">
        <span className="mm-live-spin" aria-hidden="true" />
        {item.source === "library"
          ? "Swapping it into place and telling your media manager"
          : "Checking the new file, then handing it back"}
      </p>
    </li>
  );
}

function FinishedRow({
  item,
  now,
  onOpen,
}: {
  item: FinishedFile;
  now: number;
  onOpen: (item: FinishedFile) => void;
}) {
  const tone =
    item.kind === "passed" || item.kind === "failed"
      ? "warn"
      : item.kind === "already"
        ? "same"
        : "ok";
  return (
    <li className="mm-live-done" data-testid="live-finished">
      <i className={`mm-live-dot mm-live-dot--${tone}`} aria-hidden="true" />
      <div className="mm-live-card__names">
        <button
          type="button"
          className="mm-live-card__open mm-live-card__title"
          onClick={() => onOpen(item)}
        >
          {prettyName(item.relativePath)}
        </button>
        <span className="mm-live-card__sub mm-live-card__sub--wrap">
          {finishedLine(item)}
        </span>
      </div>
      <span className="mm-live-card__sub mm-live-done__ago">
        {ago(item.finishedAt, now)}
      </span>
    </li>
  );
}

function Lane({
  id,
  label,
  count,
  hint,
  live,
  children,
  aside,
}: {
  id: string;
  label: string;
  count: string | number | null;
  hint: string;
  live?: boolean;
  children: React.ReactNode;
  aside?: React.ReactNode;
}) {
  return (
    <section
      className={`mm-live-lane mm-live-lane--${id}`}
      aria-labelledby={`live-lane-${id}`}
      data-testid={`live-lane-${id}`}
    >
      <div className="mm-live-lane__head">
        <h2 id={`live-lane-${id}`} className="mm-live-lane__label">
          {live ? <i className="mm-live-pulse" aria-hidden="true" /> : null}
          {label}
        </h2>
        {aside ??
          (count != null ? (
            <span className="mm-live-lane__count">{count}</span>
          ) : null)}
      </div>
      <p className="mm-live-lane__hint">{hint}</p>
      {children}
    </section>
  );
}

function EmptyLane({ children }: { children: React.ReactNode }) {
  return <p className="mm-live-lane__empty">{children}</p>;
}

/** The last line of a lane that has more than fits: a count, never a scrollbar inside the lane. */
function More({ count, what }: { count: number; what: string }) {
  if (count <= 0) return null;
  return (
    <li className="mm-live-lane__more">
      <Link to="/settings?tab=history&show=downloads">
        {count.toLocaleString()} more {what} →
      </Link>
    </li>
  );
}

export function ProcessingPage(): React.ReactElement {
  useActivityStreamInvalidations(LANE_KEYS, { throttleMs: 750 });
  useActivityStreamInvalidations(TOTAL_KEYS, { throttleMs: 3_000 });
  const now = useNow();
  const files = useProcessingFilesQuery(FILES_QUERY);
  const today = useProcessingOverviewStatsQuery(1);
  const filesAtOnce = useProcessingFilesAtOnceQuery();
  const libraries = useProcessingLibrariesQuery();
  const readiness = useSystemReadinessQuery();
  const pause = usePauseQuery();
  const failedJobs = useProcessingJobsInspectionQuery(
    "failed",
    FAILED_JOBS_LIMIT,
  );
  const activeJobs = useProcessingJobsInspectionQuery(
    "active",
    ACTIVE_JOBS_LIMIT,
  );
  const passes = useActivityRecentQuery({
    limit: 24,
    event_type: REMUX_PASS_COMPLETED_EVENT,
  });
  const cleans = useActivityRecentQuery({
    limit: 12,
    event_type: LIBRARY_FILE_CLEANED_EVENT,
  });
  const fileLog = useProcessingFileLog();
  const navigate = useNavigate();
  const [storyFile, setStoryFile] = useState<{
    id: number;
    name: string;
  } | null>(null);
  const [filter, setFilter] = useState<Filter>("all");

  const libraryNames = useMemo(
    () => new Map((libraries.data ?? []).map((l) => [l.id, l.name])),
    [libraries.data],
  );
  const minAge = useMemo(
    () =>
      new Map(
        (libraries.data ?? []).map((l) => [l.id, l.min_file_age_seconds]),
      ),
    [libraries.data],
  );
  const lanes = useMemo(
    () =>
      buildLanes(
        files.data?.files ?? [],
        activeJobs.data?.jobs ?? [],
        libraryNames,
        minAge,
      ),
    [files.data, activeJobs.data, libraryNames, minAge],
  );
  const finished = useMemo(() => {
    const all = [...(passes.data?.items ?? []), ...(cleans.data?.items ?? [])]
      .map(finishedFileFromEvent)
      .filter((item): item is FinishedFile => item !== null);
    return all.sort((a, b) => b.finishedAt.localeCompare(a.finishedAt));
  }, [passes.data, cleans.data]);

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
          : `/settings?tab=history&show=downloads&path=${path}`,
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

  const shows = (source: WorkSource) => filter === "all" || filter === source;
  const arriving = filter === "library" ? [] : lanes.arriving;
  const waiting = lanes.waiting.filter((i) => shows(i.source));
  const working = lanes.working.filter((i) => shows(i.source));
  const handing = lanes.handing.filter((i) => shows(i.source));
  const finishedShown = finished
    .filter((i) => shows(i.source))
    .slice(0, FINISHED_SHOWN);
  const bars = throughput(finished, now);
  const tallest = Math.max(1, ...bars);
  const lanesAtOnce = filesAtOnce.data?.effective_files_at_once ?? null;
  const stats = today.data;

  // "2 files at once · a download is left alone for 60 s after it stops changing": the two settings
  // that decide how fast files move through here. The wait is only named when every library agrees.
  const ages = new Set(
    (libraries.data ?? [])
      .filter((l) => l.enabled)
      .map((l) => l.min_file_age_seconds),
  );
  const toolbarNote = [
    lanesAtOnce != null
      ? `${lanesAtOnce} ${lanesAtOnce === 1 ? "file" : "files"} at once`
      : "",
    filesAtOnce.data?.message ?? "",
    ages.size === 1 && [...ages][0] > 0
      ? `a download is left alone for ${[...ages][0]} s after it stops changing`
      : "",
  ]
    .filter(Boolean)
    .join(" · ");

  // What needs a person, in the words Home used for the same conditions (#459).
  const needs: { key: string; text: string; to: string; action: string }[] = [];
  const noWatchedFolder = Boolean(
    libraries.data &&
    !libraries.data.some((l) => l.enabled && l.watched_folder.trim()),
  );
  if (noWatchedFolder) {
    needs.push({
      key: "setup",
      text: "Nothing to watch yet. Weir picks files up from a library's watched folder — add one, or turn an existing library on.",
      to: "/settings?tab=libraries",
      action: "Set up a library",
    });
  }
  for (const worker of readiness.data?.worker_health ?? []) {
    if (worker.status !== "degraded") continue;
    needs.push({
      key: `worker-${worker.module}`,
      text: `Background work has stopped. ${worker.detail}`,
      to: "/settings?tab=history&show=jobs",
      action: "Open jobs",
    });
  }
  const failedCount = failedJobs.data?.jobs.length ?? 0;
  if (failedCount > 0) {
    needs.push({
      key: "failed-jobs",
      text: `${failedCount === 1 ? "1 job failed" : `${failedCount >= FAILED_JOBS_LIMIT ? `${FAILED_JOBS_LIMIT}+` : failedCount} jobs failed`}. Each one says what went wrong and what to do next.`,
      to: "/settings?tab=history&show=jobs&status=failed",
      action: "Review failed jobs",
    });
  }
  if (lanes.stuck.length > 0) {
    needs.push({
      key: "stuck",
      text:
        lanes.stuck.length === 1
          ? `${prettyName(lanes.stuck[0].relative_path)} is stuck, so your media manager is still missing it. The original is untouched.`
          : `${lanes.stuck.length} files are stuck, so your media manager is still missing them. The originals are untouched.`,
      to: "/settings?tab=history&show=downloads&status=processing_failed",
      action: "Deal with them",
    });
  }

  return (
    <div className="mm-page mm-live" data-testid="processing-page">
      <PageHeader
        title="Processing"
        lead="Every file Weir is working on, from the moment it lands to the moment your media manager has it back."
        aside={
          <>
            <div className="mm-live-figure">
              <span className="mm-live-figure__label">Handed back today</span>
              <span
                className="mm-live-figure__value"
                data-testid="live-done-today"
              >
                {stats ? stats.files_processed.toLocaleString() : "…"}
              </span>
            </div>
            <div className="mm-live-figure">
              <span className="mm-live-figure__label">Saved today</span>
              <span className="mm-live-figure__value">
                {stats
                  ? formatBytes(stats.net_space_saved_bytes) || "0 B"
                  : "…"}
              </span>
            </div>
          </>
        }
      />

      {pause.data?.paused ? (
        <p className="mm-live-paused" role="status" data-testid="live-paused">
          <b>Paused.</b> {pause.data.reason} Files already being written finish;
          nothing new starts.
        </p>
      ) : null}

      {needs.length > 0 ? (
        <ul className="mm-live-needs" data-testid="live-needs">
          {needs.map((item) => (
            <li key={item.key} className="mm-live-needs__item">
              <span className="mm-live-needs__bang" aria-hidden="true">
                !
              </span>
              <span className="mm-live-needs__text">{item.text}</span>
              <Link className="mm-live-needs__link" to={item.to}>
                {item.action} →
              </Link>
            </li>
          ))}
        </ul>
      ) : null}

      <div className="mm-live-toolbar">
        <div className="mm-live-seg" role="group" aria-label="Show work from">
          {(
            [
              ["all", "Everything"],
              ["download", "New downloads"],
              ["library", "Library cleaning"],
            ] as const
          ).map(([id, label]) => (
            <button
              key={id}
              type="button"
              aria-pressed={filter === id}
              onClick={() => setFilter(id)}
            >
              {label}
            </button>
          ))}
        </div>
        <p className="mm-live-toolbar__note">
          {toolbarNote ? `${toolbarNote} · ` : ""}
          <Link to="/settings?tab=processing">change in Settings</Link>
        </p>
      </div>

      {/* Arriving and Waiting share a column below five-lane width, as do Handing back and Just
          finished; on a wide screen the two wrappers dissolve and all five sit side by side. */}
      <div className="mm-live-board">
        <div className="mm-live-lanes">
          <div className="mm-live-col mm-live-col--next">
            <Lane
              id="arriving"
              label="Arriving"
              count={arriving.length}
              hint="Making sure the downloader has finished writing"
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
              label="Waiting"
              count={waiting.length}
              hint="Ready, waiting for a free lane"
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
            label="Working"
            live={working.length > 0}
            count={null}
            hint="Writing a copy that keeps only the tracks your rules want"
            aside={
              <span className="mm-live-lane__count">
                {working.length}
                {lanesAtOnce != null ? (
                  <small>
                    {" "}
                    of {lanesAtOnce} {lanesAtOnce === 1 ? "lane" : "lanes"}
                  </small>
                ) : null}
              </span>
            }
          >
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
          </Lane>

          <div className="mm-live-col mm-live-col--done">
            <Lane
              id="handing"
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

            <Lane
              id="finished"
              label="Just finished"
              count={null}
              hint="Open one to see exactly what Weir did"
              aside={
                <Link className="mm-live-lane__link" to="/settings?tab=history">
                  History →
                </Link>
              }
            >
              {finishedShown.length ? (
                <ul className="mm-live-lane__body mm-live-lane__body--list">
                  {finishedShown.map((item) => (
                    <FinishedRow
                      key={item.id}
                      item={item}
                      now={now}
                      onOpen={openFinished}
                    />
                  ))}
                </ul>
              ) : (
                <EmptyLane>Nothing has finished recently.</EmptyLane>
              )}
            </Lane>
          </div>
        </div>
      </div>

      <section
        className="mm-live-spark"
        aria-label="Files handed back, every 5 minutes, over the last 2 hours"
      >
        <p className="mm-live-spark__label">
          Handed back, every 5 minutes, last 2 hours
        </p>
        <div className="mm-live-spark__bars" aria-hidden="true">
          {bars.map((value, index) => (
            <span
              key={index}
              className={`mm-live-spark__bar${index === bars.length - 1 ? " mm-live-spark__bar--now" : ""}`}
              style={
                {
                  "--mm-live-bar": `${Math.max(4, (value / tallest) * 100)}%`,
                } as CSSProperties
              }
              title={`${value} ${value === 1 ? "file" : "files"}`}
            />
          ))}
        </div>
        <p className="mm-live-spark__note">
          {
            "Every number here comes from Weir itself: ffmpeg’s own progress, the file’s size on disk, and when each file went back."
          }
        </p>
      </section>

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
