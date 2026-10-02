import type { NavGlyphName } from "./nav-icons";
import { SETUP_AREAS, type SetupAreaId } from "../../lib/settings/setup-areas";

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
  /** The longer sentence for the eyebrow's tooltip, when the eyebrow is a short form of it. */
  eyebrowNote?: string;
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

const SETUP_ICONS: Record<SetupAreaId, NavGlyphName> = {
  workflows: "workflows",
  connections: "managers",
  rules: "rules",
  performance: "performance",
};

const onPath = (prefix: string) => (place: NavPlace) =>
  place.pathname === prefix || place.pathname.startsWith(`${prefix}/`);

const setupItems: readonly NavItem[] = SETUP_AREAS.map((area) => ({
  id: `setup-${area.id}`,
  label: area.label,
  eyebrow: area.eyebrow,
  to: area.path,
  icon: SETUP_ICONS[area.id],
  isCurrent: onPath(area.path),
}));

/**
 * The whole side menu. Setup lists the four setup areas as items of their own; each area has its own tab row,
 * so the menu is the way between areas and the tabs are the way within one.
 */
export const NAV_GROUPS: readonly NavGroup[] = [
  {
    id: "live",
    label: "Live",
    items: [
      {
        id: "processing",
        label: "Dashboard",
        eyebrow: "Cleans new downloads and your library",
        to: "/",
        icon: "processing",
        badge: "working",
        isCurrent: (place) => place.pathname === "/",
      },
      {
        id: "history",
        label: "History",
        eyebrow: "Every file Weir handled",
        eyebrowNote: "Every file Weir has handled, and what came out",
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
        eyebrow: "Your files",
        eyebrowNote:
          "Your files, and what Weir would do: the files already on your storage, and what Weir would do to each",
        to: "/library",
        icon: "library",
        isCurrent: onPath("/library"),
      },
    ],
  },
  { id: "setup", label: "Setup", items: setupItems },
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
  /** The longer sentence for the eyebrow's tooltip, when the eyebrow is a short form of it. */
  eyebrowNote?: string;
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
      return {
        title: item.label,
        eyebrow: item.eyebrow,
        eyebrowNote: item.eyebrowNote,
        ownsHeading: true,
      };
    }
  }
  return NOT_A_MENU_PAGE;
}
