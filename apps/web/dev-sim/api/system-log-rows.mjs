/**
 * The rows of System › Logs as the simulation has them: its Activity events about Weir itself, its jobs, and server lines,
 * each with the level and category the server would give it and the facts the filters read.
 */
import { HOUR_MS, toWire } from "../wire-time.mjs";
import { jobRow } from "../engine/jobs.mjs";
import { shaped } from "../openapi/skeleton.mjs";
import { lineFor } from "./log-lines.mjs";
import {
  JOB_STATUS_LABELS,
  SOURCE,
  eventCategory,
  eventLevel,
  jobCategory,
  jobKindLabel,
  jobLevel,
  serverCategory,
  serverLevel,
} from "./system-log-rules.mjs";
import { activityItem } from "./views.mjs";

/**
 * @typedef {object} LogRow
 * @property {Record<string, unknown>} wire The row as the API returns it.
 * @property {string} source
 * @property {number} key The row's number in its source.
 * @property {number} atMs
 * @property {string} level
 * @property {string} category
 * @property {number | null} workflowId
 * @property {string} text Everything a search reads.
 * @property {number | null} jobId The job it is about, when it is about one.
 * @property {{ eventType?: string, result?: string | null, trigger?: string | null, status?: string, hasException?: boolean }} facts
 */

const SERVER_LINE_KEY_BASE = 100_000;
const FIRST_SEEDED_LINE_KEY = 1;

/** Server lines that were already in the log when the session opened: what a real install collects over a week. */
function seededServerLines(startedAt) {
  const line = (hoursAgo, level, logger, message, extra = {}) => ({
    timestamp: toWire(startedAt - hoursAgo * HOUR_MS),
    level,
    logger,
    component: "System",
    message,
    detail: null,
    traceback: null,
    correlation_id: null,
    job_id: null,
    source: null,
    ...extra,
  });
  return [
    line(30, "WARNING", "weir.platform.suite_settings.backups", "The backup folder has less than 2 GB free"),
    line(26, "ERROR", "weir.platform.suite_settings.update_service", "Could not reach the release server to look for an update", {
      traceback:
        "System.Net.Http.HttpRequestException: A connection attempt failed because the connected party did not properly respond\n   at System.Net.Http.HttpConnectionPool.ConnectAsync()\n   at Weir.Infrastructure.Settings.GitHubReleaseCatalogClient.LatestAsync()",
    }),
    line(18, "WARNING", "weir.platform.auth.rate_limit", "Too many sign-in attempts from one address; it was slowed down"),
    line(9, "INFO", "weir.platform.suite_settings.backups", "Configuration backup finished"),
    line(5, "WARNING", "weir.connection_peer", "Sonarr answered slowly (2.4 s)"),
  ].map((entry, index) => ({ ...entry, key: FIRST_SEEDED_LINE_KEY + index }));
}

/** What a failed pass writes to the server log besides its line: the exception. */
const FAILED_PASS_TRACEBACK =
  "System.InvalidOperationException: ffmpeg exited with code 1 before the output file was complete\n   at Weir.Infrastructure.Processing.RemuxPass.RemuxPassHandler.RunAsync()\n   at Weir.Infrastructure.Jobs.ProcessingJobProcessor.ProcessAsync()";

/** The job a file's event was about, as the server log words it, or null for an event about no file. */
function jobIdOf(sim, event) {
  const file = [...sim.engine.files.values()].find(
    (candidate) =>
      candidate.relativePath === event.relativePath &&
      candidate.libraryId === event.libraryId,
  );
  return file?.jobId ? String(file.jobId) : null;
}

/** The server's line for each Activity event that went wrong: a failure or a warning is written to the log; a routine step is not. */
function eventServerLines(sim) {
  return sim.engine.activity
    .all()
    .filter((event) => eventLevel(event.result) !== "success" && eventLevel(event.result) !== "info")
    .map((event) => ({
      ...lineFor(event),
      traceback: event.result === "failed" ? FAILED_PASS_TRACEBACK : null,
      job_id: jobIdOf(sim, event),
      key: SERVER_LINE_KEY_BASE + event.id,
    }));
}

/** @param {import("../sim.mjs").Sim} sim @returns {LogRow[]} */
function eventRows(sim) {
  return sim.engine.activity
    .all()
    .filter((event) => event.relativePath === null)
    .map((event) => {
      const level = eventLevel(event.result);
      const category = eventCategory(event.type);
      return {
        source: SOURCE.EVENT,
        key: event.id,
        atMs: event.createdAt,
        level,
        category,
        workflowId: event.libraryId,
        text: `${event.title} ${event.type} ${JSON.stringify(event.detail)}`,
        jobId: Number(event.detail?.job_id) || null,
        facts: {
          eventType: event.type,
          result: event.result,
          trigger: event.trigger,
        },
        wire: {
          title: event.title,
          detail: null,
          event: activityItem(event, null),
          job: null,
          server: null,
        },
      };
    });
}

const trimmed = (text) => text.replace(/\.$/, "");

/** @param {import("../sim.mjs").Sim} sim @returns {LogRow[]} */
function jobRows(sim) {
  return sim.engine.jobs.all().map((job) => {
    const row = jobRow(job);
    const attempts = row.attempt_count > 0 && job.status !== "completed";
    const kind = jobKindLabel(job.kind);
    return {
      source: SOURCE.JOB,
      key: job.id,
      atMs: job.updatedAt,
      level: jobLevel(job.status, job.lastError),
      category: jobCategory(job.kind),
      workflowId: Number(job.payload.library_id) || null,
      text: `${kind} ${JOB_STATUS_LABELS[job.status] ?? job.status} ${job.kind} ${row.dedupe_key} ${job.lastError ?? ""} ${row.payload_json}`,
      jobId: job.id,
      facts: { status: job.status },
      wire: {
        title: trimmed(row.operator_message),
        detail: attempts ? `${kind} · attempt ${row.attempt_count} of ${row.max_attempts}` : kind,
        event: null,
        job: row,
        server: null,
      },
    };
  });
}

const firstLine = (text) => text?.split("\n", 1)[0].trim() || null;

/** @param {import("../sim.mjs").Sim} sim @returns {LogRow[]} */
function serverRows(sim) {
  return [
    ...seededServerLines(sim.startedAt),
    ...eventServerLines(sim),
  ].flatMap(({ key, ...entry }) => {
    const atMs = Date.parse(entry.timestamp);
    if (Number.isNaN(atMs)) return [];
    return [
      {
        source: SOURCE.SERVER,
        key,
        atMs,
        level: serverLevel(entry.level),
        category: serverCategory(entry.logger),
        workflowId: null,
        text: `${entry.message} ${entry.logger} ${entry.traceback ?? ""}`,
        jobId: Number(entry.job_id) || null,
        facts: { hasException: Boolean(entry.traceback) },
        wire: {
          title: entry.message,
          detail: entry.detail ?? firstLine(entry.traceback),
          event: null,
          job: null,
          server: shaped("SuiteLogEntryOut", entry),
        },
      },
    ];
  });
}

/** Every row of the log, unsorted. @param {import("../sim.mjs").Sim} sim @returns {LogRow[]} */
export function allRows(sim) {
  return [...eventRows(sim), ...jobRows(sim), ...serverRows(sim)];
}

/** A row as the API returns it. @param {LogRow} row @param {Map<number, string>} workflows */
export function rowOut(row, workflows) {
  const id = `${row.source}:${row.key}`;
  const workflowName = row.workflowId === null ? null : workflows.get(row.workflowId);
  return shaped("SystemLogRowOut", {
    id,
    source: row.source,
    at: toWire(row.atMs),
    level: row.level,
    category: row.category,
    workflow:
      row.workflowId === null
        ? null
        : { id: row.workflowId, name: workflowName ?? null },
    ...row.wire,
  });
}

