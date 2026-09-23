import { Navigate, useSearchParams } from "react-router-dom";

/**
 * Where the pre-3.2 Processing tabs live now (docs/archive/live-and-library.md).
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

/** Settings tabs from 3.1 that moved to System in 3.2, so an old bookmark lands on the same thing. */
const SETTINGS_TABS_MOVED_TO_SYSTEM: Record<string, string> = {
  upgrade: "/system?tab=about",
  support: "/system?tab=about",
  backup: "/system?tab=backups",
  security: "/system?tab=security",
  logs: "/system?tab=logs",
};

/** Where a Settings tab name now lives on System, or null when it is still a Settings tab. */
export function systemAddressForSettingsTab(
  tab: string | null | undefined,
): string | null {
  return (
    SETTINGS_TABS_MOVED_TO_SYSTEM[(tab ?? "").trim().toLowerCase()] ?? null
  );
}

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
