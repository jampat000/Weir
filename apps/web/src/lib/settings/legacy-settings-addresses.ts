import { setupTabPath, type SetupTabId } from "./setup-areas";

/** The sections Settings had before its pages became the setup areas, under the name the address used. */
type FormerSettingsSection =
  | "libraries"
  | "rules"
  | "media-managers"
  | "performance"
  | "schedule"
  | "cleanup"
  | "alerts";

/** Where each former section lives now. */
const FORMER_SECTION_HOMES: Record<FormerSettingsSection, SetupTabId> = {
  libraries: "workflows",
  rules: "profiles",
  "media-managers": "managers",
  performance: "speed",
  schedule: "schedule",
  cleanup: "cleanup",
  alerts: "alerts",
};

/** Former Settings tabs that now live under System. */
const FORMER_SETTINGS_TABS_ON_SYSTEM: Record<string, string> = {
  upgrade: "/system?tab=about",
  support: "/system?tab=about",
  backup: "/system?tab=backups",
  security: "/system?tab=security",
  logs: "/system?tab=logs",
};

/** The section a `?tab=` named, including the names earlier versions used. Anything else was Workflows. */
function formerSection(candidate: string): FormerSettingsSection {
  switch (candidate) {
    case "rules":
    case "audio-subtitles":
      return "rules";
    case "media-managers":
      return "media-managers";
    case "performance":
    case "running":
    case "processing":
      return "performance";
    case "cleanup":
    case "housekeeping":
    case "maintenance":
      return "cleanup";
    case "schedule":
    case "schedules":
      return "schedule";
    case "alerts":
    case "notifications":
      return "alerts";
    default:
      return "libraries";
  }
}

/**
 * Where a former `/settings?tab=…` address lives now, so a bookmark, a saved link or the browser's history lands
 * on the same thing. Everything else in the address (`edit`, `addFrom`, `library`) goes with it.
 */
export function legacySettingsAddress(search: string): string {
  const params = new URLSearchParams(search);
  const tab = (params.get("tab") ?? "").trim().toLowerCase();
  const onSystem = FORMER_SETTINGS_TABS_ON_SYSTEM[tab];
  if (onSystem) return onSystem;

  params.delete("tab");
  const rest = params.toString();
  const home = setupTabPath(FORMER_SECTION_HOMES[formerSection(tab)]);
  return rest ? `${home}?${rest}` : home;
}
