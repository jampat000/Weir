import { apiFetch, readJson, requireOk } from "../api/client";
import { filenameFromDisposition } from "../api/activity-api";
import type { Schema } from "../api/types";

/** One page of System › Logs: Weir's events, jobs and server log as one list, newest first. */
export type SystemLogPage = Schema<"SystemLogOut">;
export type SystemLogRow = Schema<"SystemLogRowOut">;
export type SystemLogSource = SystemLogRow["source"];
export type SystemLogLevel = SystemLogRow["level"];
export type SystemLogCategory = SystemLogRow["category"];

/** What a request for the log can narrow it by. A list is sent as one comma-separated value; an empty one is no filter. */
export type SystemLogQuery = {
  source?: readonly SystemLogSource[];
  level?: readonly SystemLogLevel[];
  category?: readonly SystemLogCategory[];
  /** A workflow's id. */
  workflow?: number;
  q?: string;
  /** ISO 8601 instants. */
  from?: string;
  to?: string;
  /** One job's id: its row, the events that name it and the server lines written while it ran. */
  job?: number;
  event_type?: string;
  result?: string;
  trigger?: string;
  status?: readonly string[];
  has_exception?: boolean;
};

/** A page of the log: the next page starts at the `cursor` the page before gave. */
export type SystemLogPageRequest = SystemLogQuery & {
  cursor?: string;
  limit?: number;
};

/** The filters as query parameters, shared by the list and its export. */
function filterParams(query: SystemLogQuery): URLSearchParams {
  const params = new URLSearchParams();
  for (const name of ["source", "level", "category", "status"] as const) {
    const values = query[name];
    if (values?.length) params.set(name, values.join(","));
  }
  for (const name of [
    "q",
    "from",
    "to",
    "event_type",
    "result",
    "trigger",
  ] as const) {
    const value = query[name];
    if (value) params.set(name, value);
  }
  for (const name of ["workflow", "job"] as const) {
    const value = query[name];
    if (value !== undefined) params.set(name, String(Math.trunc(value)));
  }
  if (query.has_exception !== undefined) {
    params.set("has_exception", String(query.has_exception));
  }
  return params;
}

export function systemLogPath(request: SystemLogPageRequest): string {
  const params = filterParams(request);
  if (request.cursor) params.set("cursor", request.cursor);
  if (request.limit !== undefined) params.set("limit", String(request.limit));
  const text = params.toString();
  return text ? `/api/v1/system/log?${text}` : "/api/v1/system/log";
}

export async function fetchSystemLog(
  request: SystemLogPageRequest,
): Promise<SystemLogPage> {
  const path = systemLogPath(request);
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not load the log");
  return readJson<SystemLogPage>(response);
}

export function systemLogExportPath(
  format: "csv" | "json",
  query: SystemLogQuery,
): string {
  const params = filterParams(query);
  params.set("format", format);
  return `/api/v1/system/log/export?${params.toString()}`;
}

/** The log for these filters as a file. The caller hands the blob to the browser. */
export async function fetchSystemLogExport(
  format: "csv" | "json",
  query: SystemLogQuery,
): Promise<{ blob: Blob; filename: string }> {
  const path = systemLogExportPath(format, query);
  const response = await apiFetch(path, {
    headers: { Accept: format === "json" ? "application/json" : "text/csv" },
  });
  await requireOk(path, response, "Could not export the log");
  return {
    blob: await response.blob(),
    filename: filenameFromDisposition(
      response.headers.get("Content-Disposition"),
      `weir-log.${format}`,
    ),
  };
}
