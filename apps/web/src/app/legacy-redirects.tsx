import { Navigate, useLocation, useSearchParams } from "react-router-dom";

import { legacySettingsAddress } from "../lib/settings/legacy-settings-addresses";
import { setupTabPath } from "../lib/settings/setup-areas";

/** Where each former Processing tab lives now, so a saved bookmark lands on the same thing. */
const PROCESSING_TAB_HOMES: Record<string, string> = {
  overview: "/",
  files: "/history",
  libraries: setupTabPath("workflows"),
  "audio-subtitles": setupTabPath("profiles"),
  schedules: setupTabPath("schedule"),
  library: "/library",
  jobs: "/system?tab=logs&show=jobs",
  maintenance: setupTabPath("cleanup"),
};

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

/** Settings is the setup areas, and System for what moved there; the old address still lands on the same thing. */
export function LegacySettingsRedirect() {
  const { search, hash } = useLocation();
  return <Navigate to={`${legacySettingsAddress(search)}${hash}`} replace />;
}

/** Activity is System › Logs now; a file's own story is in History. */
export function LegacyActivityRedirect() {
  return <Navigate to="/system?tab=logs" replace />;
}
