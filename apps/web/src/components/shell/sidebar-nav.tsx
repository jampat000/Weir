import { forwardRef, type Ref } from "react";
import { Link, useLocation } from "react-router-dom";

import { NavGlyph } from "./nav-icons";
import { NAV_GROUPS, type NavBadgeKind, type NavItem } from "./nav-model";

/** What rides on each badge: the number, and the words a screen reader says for it. */
type Badges = Readonly<Record<NavBadgeKind, number>>;

const BADGE_WORDS: Record<NavBadgeKind, string> = {
  working: "working",
  "needs-you": "need you",
};

const BADGE_TEST_IDS: Record<NavBadgeKind, string> = {
  working: "nav-processing-working",
  "needs-you": "nav-history-needs-you",
};

type SidebarLinkProps = {
  item: NavItem;
  current: boolean;
  count: number;
  onNavigate: () => void;
};

const SidebarLink = forwardRef<HTMLAnchorElement, SidebarLinkProps>(
  function SidebarLink({ item, current, count, onNavigate }, ref) {
    const badge = item.badge && count > 0 ? item.badge : null;
    const words = badge ? `${count} ${BADGE_WORDS[badge]}` : null;
    return (
      <Link
        ref={ref}
        to={item.to}
        className="mm-sidebar-link"
        aria-current={current ? "page" : undefined}
        aria-label={words ? `${item.label}, ${words}` : undefined}
        title={item.label}
        onClick={onNavigate}
      >
        <span className="mm-sidebar-link-icon" aria-hidden="true">
          <NavGlyph name={item.icon} />
        </span>
        <span className="mm-sidebar-link-label">{item.label}</span>
        {badge ? (
          <span
            className={`mm-sidebar-link-badge mm-sidebar-link-badge--${badge}`}
            data-testid={BADGE_TEST_IDS[badge]}
            title={words ?? undefined}
          >
            {count}
          </span>
        ) : null}
      </Link>
    );
  },
);

type SidebarNavProps = {
  badges: Badges;
  /** Takes focus when a phone's menu opens. */
  firstLinkRef: Ref<HTMLAnchorElement>;
  onNavigate: () => void;
};

/**
 * The side menu: groups under quiet headings, each item a link. The current page is named for a
 * screen reader with `aria-current`, which the address's query decides for Settings' sections.
 */
export function SidebarNav({
  badges,
  firstLinkRef,
  onNavigate,
}: SidebarNavProps) {
  const { pathname, search } = useLocation();
  const place = { pathname, search };
  return (
    <nav className="mm-sidebar-nav" aria-label="Primary">
      {NAV_GROUPS.map((group, groupIndex) => {
        const headingId = `mm-nav-group-${group.id}`;
        return (
          <div
            key={group.id}
            className="mm-sidebar-group"
            role="group"
            aria-labelledby={headingId}
          >
            <p id={headingId} className="mm-sidebar-group__label">
              {group.label}
            </p>
            {group.items.map((item, itemIndex) => (
              <SidebarLink
                key={item.id}
                ref={
                  groupIndex === 0 && itemIndex === 0 ? firstLinkRef : undefined
                }
                item={item}
                current={item.isCurrent(place)}
                count={item.badge ? badges[item.badge] : 0}
                onNavigate={onNavigate}
              />
            ))}
          </div>
        );
      })}
    </nav>
  );
}
