export type SettingsSectionId =
  | "libraries"
  | "rules"
  | "media-managers"
  | "performance"
  | "schedule"
  | "cleanup"
  | "alerts";

export type SettingsSection = Readonly<{
  id: SettingsSectionId;
  /** The name in the side menu and the page title. */
  label: string;
  /** The line above the title: what the section is for. */
  eyebrow: string;
}>;

/** The sections in the order someone sets Weir up: where the media is, what to keep, who to tell, then how and when to work. */
export const SETTINGS_SECTIONS: readonly SettingsSection[] = [
  {
    id: "libraries",
    label: "Workflows",
    eyebrow: "Where your media is, and how each folder is cleaned",
  },
  {
    id: "rules",
    label: "Rules",
    eyebrow: "What to keep and what to take out of every file",
  },
  {
    id: "media-managers",
    label: "Media managers",
    eyebrow: "The apps Weir hands cleaned files back to",
  },
  {
    id: "performance",
    label: "Performance",
    eyebrow: "How many files Weir works on at once",
  },
  {
    id: "schedule",
    label: "Schedule",
    eyebrow: "When Weir is allowed to work",
  },
  {
    id: "cleanup",
    label: "Cleanup",
    eyebrow: "Leftover files, and when Weir tidies them away",
  },
  {
    id: "alerts",
    label: "Alerts",
    eyebrow: "Who Weir tells, and about what",
  },
];

/** The section Settings opens on, which needs no `tab` in its address. */
const DEFAULT_SECTION: SettingsSectionId = "libraries";

/** A section from the address's `tab`, including the names earlier versions used. */
export function normalizeSettingsSection(
  candidate: string | null | undefined,
): SettingsSectionId {
  switch ((candidate || "").trim().toLowerCase()) {
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
      return DEFAULT_SECTION;
  }
}

/** The section a Settings address (`?tab=…`) names. */
export function settingsSectionFromSearch(search: string): SettingsSectionId {
  return normalizeSettingsSection(new URLSearchParams(search).get("tab"));
}

export function settingsSection(id: SettingsSectionId): SettingsSection {
  return (
    SETTINGS_SECTIONS.find((section) => section.id === id) ??
    SETTINGS_SECTIONS[0]
  );
}

/** Where a section lives. The default section needs no `tab`. */
export function settingsSectionPath(id: SettingsSectionId): string {
  return id === DEFAULT_SECTION ? "/settings" : `/settings?tab=${id}`;
}
