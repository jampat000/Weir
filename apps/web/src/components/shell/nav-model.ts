import {
  SETTINGS_SECTIONS,
  settingsSectionFromSearch,
  settingsSectionPath,
  type SettingsSectionId,
} from "../../lib/settings/settings-sections";
import type { NavGlyphName } from "./nav-icons";

/** Where the person is: the part of the address the side menu reads. */
export type NavPlace = { pathname: string; search: string };

/** Which count, if any, rides on a menu item. */
export type NavBadgeKind = "working" | "needs-you";

export type NavItem = Readonly<{
  id: string;
  /** The name in the menu, and the page title. */
  label: string;
  /** The line above the page title: what the page is for. */
  eyebrow: string;
  to: string;
  icon: NavGlyphName;
  badge?: NavBadgeKind;
  isCurrent: (place: NavPlace) => boolean;
}>;

export type NavGroup = Readonly<{
  id: string;
  label: string;
  items: readonly NavItem[];
}>;

const SETTINGS_ICONS: Record<SettingsSectionId, NavGlyphName> = {
  libraries: "workflows",
  rules: "rules",
  "media-managers": "managers",
  performance: "performance",
  schedule: "schedule",
  cleanup: "cleanup",
  alerts: "alerts",
};

const onPath = (prefix: string) => (place: NavPlace) =>
  place.pathname === prefix || place.pathname.startsWith(`${prefix}/`);

const settingsItems: readonly NavItem[] = SETTINGS_SECTIONS.map((section) => ({
  id: `settings-${section.id}`,
  label: section.label,
  eyebrow: section.eyebrow,
  to: settingsSectionPath(section.id),
  icon: SETTINGS_ICONS[section.id],
  isCurrent: (place) =>
    onPath("/settings")(place) &&
    settingsSectionFromSearch(place.search) === section.id,
}));

/**
 * The whole side menu. Setup lists Settings' sections as items of their own, so the menu is the one
 * way between them and Settings has no tab row of its own; each keeps its `/settings?tab=…` address.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  {
    id: "live",
    label: "Live",
    items: [
      {
        id: "processing",
        label: "Processing",
        eyebrow: "Cleans new downloads and your library",
        to: "/",
        icon: "processing",
        badge: "working",
        isCurrent: (place) => place.pathname === "/",
      },
      {
        id: "history",
        label: "History",
        eyebrow: "Every file Weir has handled, and what came out",
        to: "/history",
        icon: "history",
        badge: "needs-you",
        isCurrent: onPath("/history"),
      },
    ],
  },
  {
    id: "library",
    label: "Your library",
    items: [
      {
        id: "library",
        label: "Library",
        eyebrow:
          "The files already on your storage, and what Weir would do to each",
        to: "/library",
        icon: "library",
        isCurrent: onPath("/library"),
      },
    ],
  },
  { id: "setup", label: "Setup", items: settingsItems },
  {
    id: "weir",
    label: "Weir",
    items: [
      {
        id: "system",
        label: "System",
        eyebrow: "Weir itself: what it runs, what it keeps, who can sign in",
        to: "/system",
        icon: "system",
        isCurrent: onPath("/system"),
      },
    ],
  },
];

export type PageMeta = Readonly<{
  title: string;
  eyebrow: string;
  /** False on a screen that is not one of the menu's pages (Not found): it carries its own heading. */
  ownsHeading: boolean;
}>;

const NOT_A_MENU_PAGE: PageMeta = {
  title: "Weir",
  eyebrow: "",
  ownsHeading: false,
};

/** The title and eyebrow the header shows for where the person is. */
export function pageMeta(place: NavPlace): PageMeta {
  for (const group of NAV_GROUPS) {
    const item = group.items.find((candidate) => candidate.isCurrent(place));
    if (item) {
      return { title: item.label, eyebrow: item.eyebrow, ownsHeading: true };
    }
  }
  return NOT_A_MENU_PAGE;
}
