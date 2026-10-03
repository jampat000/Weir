import { Navigate, useLocation, useSearchParams } from "react-router-dom";

import { legacySettingsAddress } from "../lib/settings/legacy-settings-addresses";
import { setupTabPath } from "../lib/settings/setup-areas";
import { ACTIVITY_PATH } from "../pages/activity/activity-links";

/** Where each former Processing tab lives now, so a saved bookmark lands on the same thing. */
const PROCESSING_TAB_HOMES: Record<string, string> = {
  overview: "/",
  files: ACTIVITY_PATH,
  libraries: setupTabPath("workflows"),
  "audio-subtitles": setupTabPath("profiles"),
  schedules: setupTabPath("schedule"),
  library: "/library",
  jobs: "/system?tab=logs&source=job",
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

/** The Activity page was called History; every parameter an old link carried still means the same thing. */
export function LegacyHistoryRedirect() {
  const { search, hash } = useLocation();
  return <Navigate to={`${ACTIVITY_PATH}${search}${hash}`} replace />;
}
