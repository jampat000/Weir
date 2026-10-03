import { useLocation } from "react-router-dom";

import { HeaderStatus } from "./header-status";
import { pageMeta } from "./nav-model";
import { PauseControl } from "./pause-control";
import {
  useHeaderButtonsSlotRef,
  useHeaderSlotRef,
  useHeaderTabsSlotRef,
  usePageEyebrow,
} from "./shell-header-context";
import { ThemeToggle } from "./theme-toggle";

type ShellHeaderProps = {
  menuOpen: boolean;
  onToggleMenu: () => void;
};

/**
 * The bar above every page, sticky: the page's eyebrow and title (with its tabs after the title, when it
 * has any), a slot the page fills with its own control, the page's own buttons, then the status pill, Pause and the
 * theme switch. On a phone it also carries the Menu button.
 */
export function ShellHeader({ menuOpen, onToggleMenu }: ShellHeaderProps) {
  const { pathname, search } = useLocation();
  const meta = pageMeta({ pathname, search });
  const pageEyebrow = usePageEyebrow();
  const slotRef = useHeaderSlotRef();
  const tabsSlotRef = useHeaderTabsSlotRef();
  const buttonsSlotRef = useHeaderButtonsSlotRef();
  const eyebrow = pageEyebrow ?? meta.eyebrow;
  const Title = meta.ownsHeading ? "h1" : "p";

  return (
    <header className="mm-header" data-testid="shell-header">
      {/* Phones only (hidden on wider screens by CSS): opens the side menu. */}
      <button
        type="button"
        className="mm-header__menu"
        data-testid="shell-nav-toggle"
        aria-controls="mm-primary-sidebar"
        aria-expanded={menuOpen}
        onClick={onToggleMenu}
      >
        <svg
          viewBox="0 0 24 24"
          width="16"
          height="16"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          aria-hidden="true"
        >
          <path d="M4 7h16" />
          <path d="M4 12h16" />
          <path d="M4 17h16" />
        </svg>
        <span>Menu</span>
      </button>
      <div className="mm-header__titles">
        {eyebrow ? (
          <p
            className="mm-header__eyebrow"
            title={pageEyebrow ? eyebrow : (meta.eyebrowNote ?? eyebrow)}
          >
            {eyebrow}
          </p>
        ) : null}
        <div className="mm-header__line">
          <Title className="mm-header__title">{meta.title}</Title>
          <div className="mm-header__tabs" ref={tabsSlotRef} />
        </div>
      </div>
      <div className="mm-header__slot" ref={slotRef} />
      <div className="mm-header__buttons" ref={buttonsSlotRef} />
      <div className="mm-header__actions">
        <HeaderStatus />
        <PauseControl />
        <ThemeToggle />
      </div>
    </header>
  );
}
