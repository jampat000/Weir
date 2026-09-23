import { useQuery } from "@tanstack/react-query";
import { useEffect, useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { DirectPlayLine } from "../../components/processing/direct-play-line";
import { PageHeader } from "../../components/shell/page-header";
import { useMeQuery } from "../../lib/auth/queries";
import { usePauseQuery } from "../../lib/pause/pause-queries";
import { mmActionButtonClass } from "../../lib/ui/mm-control-roles";
import {
  PROCESSING_FILE_STATUS_LABELS,
  fetchProcessingFileLog,
  processingFileLogDownloadPath,
  type ProcessingFile,
} from "../../lib/processing/files-api";
import {
  useProcessingFilesQuery,
  useRequeueProcessingFiles,
} from "../../lib/processing/files-queries";
import { HistoryFileActions, fileGuidance } from "./history-file-actions";
import { useProcessingLibrariesQuery } from "../../lib/processing/libraries-queries";
import {
  HISTORY_GROUPS,
  agoWords,
  fileName,
  historyGroupOf,
  inGroup,
  latestPass,
  newestFirst,
  sizeWords,
  sizesFromRecord,
  tookWords,
  tracksFromRecord,
  type HistoryGroup,
} from "./history-model";

/** How far back History looks, as the server's within_days. */
const PERIODS: { id: string; label: string; days?: number }[] = [
  { id: "1", label: "Today", days: 1 },
  { id: "7", label: "Last 7 days", days: 7 },
  { id: "30", label: "Last 30 days", days: 30 },
  { id: "all", label: "Everything kept" },
];

const REFRESH_MS = 5000;

/**
 * History: every file Weir has touched, what it was, what Weir did and what came out (James, 23 Sep 2026,
 * canvas board 2A). A place of its own because a file's story is neither a live lane nor a system log:
 * System › Logs keeps Weir's own events, and this keeps the files.
 */
export function HistoryPage() {
  const [params, setParams] = useSearchParams();
  const group = (HISTORY_GROUPS.find((g) => g.id === params.get("show"))?.id ??
    "all") as HistoryGroup;
  const period =
    PERIODS.find((p) => p.id === params.get("within")) ?? PERIODS[1];
  const libraryId = Number(params.get("library")) || undefined;
  const selectedId = Number(params.get("file")) || null;
  const [search, setSearch] = useState(params.get("q") ?? "");
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 15000);
    return () => window.clearInterval(timer);
  }, []);

  const files = useProcessingFilesQuery({
    limit: 1000,
    within_days: period.days,
    library_id: libraryId,
    path_contains: params.get("q") ?? undefined,
  });
  const libraries = useProcessingLibrariesQuery();
  const me = useMeQuery();
  const editable = me.data?.role === "operator" || me.data?.role === "admin";
  const requeueFailed = useRequeueProcessingFiles();
  const [bulkNotice, setBulkNotice] = useState<string | null>(null);

  // A running pass moves every few seconds; keep the list and the open file current while one does.
  const anyWorking = (files.data?.files ?? []).some(
    (f) => historyGroupOf(f) === "working",
  );
  useEffect(() => {
    if (!anyWorking) return;
    const timer = window.setInterval(() => void files.refetch(), REFRESH_MS);
    return () => window.clearInterval(timer);
  }, [anyWorking, files]);

  const all = useMemo(() => newestFirst(files.data?.files ?? []), [files.data]);
  const shown = all.filter((f) => inGroup(f, group));
  const counts = Object.fromEntries(
    HISTORY_GROUPS.map((g) => [
      g.id,
      all.filter((f) => inGroup(f, g.id)).length,
    ]),
  ) as Record<HistoryGroup, number>;
  const selected = all.find((f) => f.id === selectedId) ?? shown[0] ?? null;

  function setParam(name: string, value: string | null) {
    const next = new URLSearchParams(params);
    if (value === null || value === "") next.delete(name);
    else next.set(name, value);
    setParams(next, { replace: true });
  }

  return (
    <div className="mm-page" data-testid="history-page">
      <PageHeader
        title="History"
        lead="Every file Weir has touched: what it was, what Weir did, and what came out."
      />
      <div className="mm-history-filters" data-testid="history-filters">
        <form
          className="mm-history-search"
          role="search"
          onSubmit={(event) => {
            event.preventDefault();
            setParam("q", search.trim());
          }}
        >
          <input
            className="mm-input"
            type="search"
            aria-label="Find a file"
            placeholder="Find a file"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            onBlur={() => setParam("q", search.trim())}
          />
        </form>
        <div className="mm-history-chips" role="group" aria-label="Show">
          {HISTORY_GROUPS.map((g) => (
            <button
              key={g.id}
              type="button"
              className="mm-history-chip"
              aria-pressed={g.id === group}
              onClick={() => setParam("show", g.id === "all" ? null : g.id)}
            >
              {g.label}{" "}
              <span className="mm-history-chip__count">{counts[g.id]}</span>
            </button>
          ))}
        </div>
        <div className="mm-history-scope">
          <select
            className="mm-input"
            aria-label="Library"
            value={libraryId ?? ""}
            onChange={(event) => setParam("library", event.target.value)}
          >
            <option value="">All libraries</option>
            {(libraries.data ?? []).map((library) => (
              <option key={library.id} value={library.id}>
                {library.name}
              </option>
            ))}
          </select>
          <select
            className="mm-input"
            aria-label="How far back"
            value={period.id}
            onChange={(event) => setParam("within", event.target.value)}
          >
            {PERIODS.map((p) => (
              <option key={p.id} value={p.id}>
                {p.label}
              </option>
            ))}
          </select>
        </div>
      </div>

      {group === "failed" && editable && counts.failed > 0 ? (
        <div className="mm-history-bulk">
          <button
            type="button"
            className={mmActionButtonClass({ variant: "secondary" })}
            disabled={requeueFailed.isPending}
            onClick={() => {
              setBulkNotice(null);
              requeueFailed
                .mutateAsync({
                  file_status: "processing_failed",
                  library_id: libraryId,
                  limit: 1000,
                })
                .then((result) => setBulkNotice(result.detail))
                .catch(() =>
                  setBulkNotice("Those files could not be queued again."),
                );
            }}
          >
            Try every failed file again
          </button>
          {bulkNotice ? (
            <span className="mm-history-note" role="status">
              {bulkNotice}
            </span>
          ) : null}
        </div>
      ) : null}

      {files.isError ? (
        <p className="mm-history-empty" role="alert">
          Weir could not read its file history. Check that it is still running,
          then refresh.
        </p>
      ) : files.isLoading ? (
        <p className="mm-history-empty">Reading history…</p>
      ) : shown.length === 0 ? (
        <p className="mm-history-empty">
          {all.length === 0
            ? "Nothing yet. Every file Weir picks up will be listed here, from the moment it arrives."
            : "No file matches. Try All, or look further back."}
        </p>
      ) : (
        <div className="mm-history-body">
          <HistoryList
            files={shown}
            selectedId={selected?.id ?? null}
            now={now}
            onPick={(id) => setParam("file", String(id))}
          />
          {selected ? (
            <HistoryDetail file={selected} now={now} editable={editable} />
          ) : null}
        </div>
      )}
    </div>
  );
}

/** What Weir did, in a few words, for the list. */
function whatWeirDid(file: ProcessingFile): string {
  if (file.status === "processing") {
    return file.progress_percent != null
      ? `Writing · ${Math.round(file.progress_percent)}%`
      : "Working on it";
  }
  if (file.quarantined) return "Held after repeated failures";
  return PROCESSING_FILE_STATUS_LABELS[file.status] ?? file.status;
}

function HistoryList({
  files,
  selectedId,
  now,
  onPick,
}: {
  files: ProcessingFile[];
  selectedId: number | null;
  now: number;
  onPick: (id: number) => void;
}) {
  return (
    <table className="mm-history-table">
      <thead>
        <tr>
          <th scope="col">File</th>
          <th scope="col">What happened</th>
          <th scope="col">When</th>
        </tr>
      </thead>
      <tbody>
        {files.map((file) => {
          const group = historyGroupOf(file);
          return (
            <tr
              key={file.id}
              className={file.id === selectedId ? "is-selected" : undefined}
              aria-selected={file.id === selectedId}
            >
              <td>
                <button
                  type="button"
                  className="mm-history-file"
                  onClick={() => onPick(file.id)}
                  title={file.relative_path}
                >
                  <span className="mm-history-file__name">
                    {fileName(file.relative_path)}
                  </span>
                  <span className="mm-history-file__sub">
                    {[file.library_name, sizeWords(file.size_bytes)]
                      .filter(Boolean)
                      .join(" · ")}
                  </span>
                </button>
              </td>
              <td>
                <span
                  className={`mm-history-what mm-history-what--${group ?? "other"}`}
                >
                  {whatWeirDid(file)}
                </span>
              </td>
              <td className="mm-history-when">
                {agoWords(file.updated_at, now)}
              </td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}

function HistoryDetail({
  file,
  now,
  editable,
}: {
  file: ProcessingFile;
  now: number;
  editable: boolean;
}) {
  const working = file.status === "processing";
  const pause = usePauseQuery();
  const guidance = fileGuidance(file, pause.data?.paused === true);
  const record = useQuery({
    queryKey: ["processing", "files", file.id, "log", file.updated_at],
    queryFn: () => fetchProcessingFileLog(file.id),
  });
  const pass = latestPass(record.data?.entries ?? []);
  const tracks = pass ? tracksFromRecord(pass.detail) : [];
  const sizes = pass ? sizesFromRecord(pass.detail) : null;
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
        {fileName(file.relative_path)}
      </h2>
      <p className="mm-history-detail__lead">
        {PROCESSING_FILE_STATUS_LABELS[file.status] ?? file.status}
        {file.status_reason ? `. ${file.status_reason}` : ""}
        {took && !working ? ` Took ${took}.` : ""}
      </p>

      {guidance.title ? (
        <div className="mm-history-next">
          <p className="mm-history-next__title">{guidance.title}</p>
          <p className="mm-history-note">{guidance.next}</p>
        </div>
      ) : null}
      <DirectPlayLine
        directPlay={file.direct_play}
        testId="history-direct-play"
      />
      <HistoryFileActions key={file.id} file={file} editable={editable} />

      {working ? (
        <div className="mm-history-progress">
          <div
            className="mm-history-progress__bar"
            role="progressbar"
            aria-valuemin={0}
            aria-valuemax={100}
            aria-valuenow={Math.round(file.progress_percent ?? 0)}
            aria-label="Through the file"
          >
            <i style={{ width: `${file.progress_percent ?? 0}%` }} />
          </div>
          <dl className="mm-history-figures">
            <div>
              <dt>Through the file</dt>
              <dd>{Math.round(file.progress_percent ?? 0)}%</dd>
            </div>
            {file.progress_speed ? (
              <div>
                <dt>Speed</dt>
                <dd>{file.progress_speed}</dd>
              </div>
            ) : null}
            {file.progress_elapsed_seconds != null ? (
              <div>
                <dt>Running for</dt>
                <dd>{tookWords(file.progress_elapsed_seconds)}</dd>
              </div>
            ) : null}
            {file.progress_eta_seconds != null ? (
              <div>
                <dt>About</dt>
                <dd>{tookWords(file.progress_eta_seconds)} left</dd>
              </div>
            ) : null}
          </dl>
        </div>
      ) : sizes ? (
        <dl className="mm-history-figures">
          <div>
            <dt>Before</dt>
            <dd>{sizeWords(sizes.before)}</dd>
          </div>
          <div>
            <dt>After</dt>
            <dd>{sizeWords(sizes.after)}</dd>
          </div>
          <div>
            <dt>Saved</dt>
            <dd className="mm-history-saved">{sizeWords(sizes.saved)}</dd>
          </div>
        </dl>
      ) : null}

      {record.isLoading ? (
        <p className="mm-history-note">Reading this file&rsquo;s record…</p>
      ) : record.isError ? (
        <p className="mm-history-note" role="alert">
          Weir could not read this file&rsquo;s record.
        </p>
      ) : !pass ? (
        <p className="mm-history-note">
          {working || historyGroupOf(file) === "working"
            ? "Weir has not finished a pass over this file yet. What it kept and removed shows here once it has."
            : "Weir kept no record of a pass over this file. Records older than the History setting are removed."}
        </p>
      ) : (
        <>
          {tracks.length > 0 ? (
            <table className="mm-history-tracks">
              <caption>
                {tracks.filter((t) => t.kept).length} tracks kept,{" "}
                {tracks.filter((t) => !t.kept).length} removed
              </caption>
              <tbody>
                {tracks.map((track, index) => (
                  <tr key={`${track.kind}-${index}`}>
                    <th scope="row">{track.kind}</th>
                    <td className={track.kept ? undefined : "is-removed"}>
                      {track.what}
                    </td>
                    <td
                      className={
                        track.kept ? "mm-history-kept" : "mm-history-gone"
                      }
                    >
                      {track.kept ? "Kept" : track.why || "Removed"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          ) : null}
          {pass.story.length > 0 ? (
            <ol className="mm-history-story" aria-label="What happened">
              {pass.story.map((step, index) => (
                <li key={index} className={`is-${step.tone}`}>
                  <b>{step.heading}</b>
                  <span>{step.sentence}</span>
                </li>
              ))}
            </ol>
          ) : null}
          <p className="mm-history-record-meta">
            Recorded {agoWords(pass.recorded_at, now)}
          </p>
        </>
      )}

      <div className="mm-history-links">
        {working ? <Link to="/">See it on Processing →</Link> : null}
        <a href={processingFileLogDownloadPath(file.id)}>
          Download its record →
        </a>
      </div>
    </section>
  );
}
