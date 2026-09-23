import { Navigate, useSearchParams } from "react-router-dom";

/**
 * Where the pre-3.2 Processing tabs live now (docs/exec-plans/active/live-and-library.md).
 * 3.0.0 dropped every old address because nobody had installed it yet; 3.1 has been installed,
 * so its bookmarks and any links a user saved land on the same thing in its new place.
 */
const PROCESSING_TAB_HOMES: Record<string, string> = {
  overview: "/",
  files: "/history",
  libraries: "/settings?tab=libraries",
  "audio-subtitles": "/settings?tab=rules",
  schedules: "/settings?tab=schedule",
  library: "/library",
  jobs: "/system?tab=logs&show=jobs",
  maintenance: "/settings?tab=cleanup",
};

/** Filters the old Files and Jobs tabs understood, carried over so a saved filter still works. */
const CARRIED_PARAMS = ["status", "path"];

export function LegacyProcessingRedirect() {
  const [params] = useSearchParams();
  const target = PROCESSING_TAB_HOMES[params.get("tab") ?? "overview"] ?? "/";
  const url = new URL(target, window.location.origin);
  for (const name of CARRIED_PARAMS) {
    const value = params.get(name);
    if (value) url.searchParams.set(name, value);
  }
  return <Navigate to={`${url.pathname}${url.search}`} replace />;
}

/** Activity is System › Logs now; a file's own story is in History. */
export function LegacyActivityRedirect() {
  return <Navigate to="/system?tab=logs" replace />;
}
