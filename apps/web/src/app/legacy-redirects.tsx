import { Navigate, useSearchParams } from "react-router-dom";

/** Where each former Processing tab lives now, so a saved bookmark lands on the same thing. */
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

/** Former Settings tabs that now live under System, so an old bookmark lands on the same thing. */
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

/** Filters the former Files and Jobs tabs understood, carried over so a saved filter still works. */
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
