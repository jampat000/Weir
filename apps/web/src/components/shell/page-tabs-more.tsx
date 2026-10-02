import {
  useCallback,
  useEffect,
  useId,
  useRef,
  useState,
  type KeyboardEvent,
  type Ref,
} from "react";

import { useCloseOnOutsideAndEscape } from "../../lib/ui/use-close-on-outside";
import { NavIconChevronDown } from "./nav-icons";

type PageTabsMoreProps<Id extends string> = {
  /** Names the menu for a screen reader. */
  menuLabel: string;
  /** The tabs that did not fit, in order. */
  tabs: readonly Readonly<{ id: Id; label: string }>[];
  /** A tab was chosen from the menu. */
  onChoose: (id: Id) => void;
};

/** What the menu's arrow keys and Home and End do: the entry to focus next, or null for any other key. */
function entryAfterKey(
  key: string,
  index: number,
  count: number,
): number | null {
  switch (key) {
    case "ArrowDown":
      return (index + 1) % count;
    case "ArrowUp":
      return (index - 1 + count) % count;
    case "Home":
      return 0;
    case "End":
      return count - 1;
    default:
      return null;
  }
}

/**
 * "More ▾" at the end of a row of tabs, opening a menu of the tabs that did not fit. It is a menu button, never
 * drawn as a chosen tab: the chosen tab always has a place in the row.
 */
export function PageTabsMore<Id extends string>({
  menuLabel,
  tabs,
  onChoose,
}: PageTabsMoreProps<Id>) {
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const menuRef = useRef<HTMLDivElement>(null);
  const menuId = useId();
  const close = useCallback(() => setOpen(false), []);
  useCloseOnOutsideAndEscape(open, close, containerRef);

  const entries = () =>
    Array.from(
      menuRef.current?.querySelectorAll<HTMLElement>("[role=menuitem]") ?? [],
    );

  useEffect(() => {
    if (open) entries()[0]?.focus();
  }, [open]);

  const closeAndReturn = () => {
    setOpen(false);
    buttonRef.current?.focus();
  };

  const onMenuKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === "Escape") {
      event.preventDefault();
      closeAndReturn();
      return;
    }
    if (event.key === "Tab") {
      setOpen(false);
      return;
    }
    const all = entries();
    const next = entryAfterKey(
      event.key,
      all.indexOf(document.activeElement as HTMLElement),
      all.length,
    );
    if (next === null) return;
    event.preventDefault();
    all[next]?.focus();
  };

  return (
    <div className="mm-page-tabs__more" ref={containerRef}>
      <button
        ref={buttonRef}
        type="button"
        className="mm-page-tabs__more-button"
        aria-haspopup="menu"
        aria-expanded={open}
        aria-controls={open ? menuId : undefined}
        onClick={() => setOpen((value) => !value)}
        onKeyDown={(event) => {
          if (event.key !== "ArrowDown" || open) return;
          event.preventDefault();
          setOpen(true);
        }}
      >
        More
        <NavIconChevronDown className="mm-page-tabs__more-chevron" />
      </button>
      {open ? (
        <div
          ref={menuRef}
          id={menuId}
          role="menu"
          tabIndex={-1}
          aria-label={menuLabel}
          className="mm-page-tabs__menu"
          onKeyDown={onMenuKeyDown}
        >
          {tabs.map(({ id, label }) => (
            <button
              key={id}
              type="button"
              role="menuitem"
              className="mm-page-tabs__menu-item"
              onClick={() => {
                setOpen(false);
                onChoose(id);
              }}
            >
              {label}
            </button>
          ))}
        </div>
      ) : null}
    </div>
  );
}

/** The out-of-sight twin of the More button, as wide as it is. */
export function PageTabsMoreProbe({
  probeRef,
}: {
  probeRef: Ref<HTMLSpanElement>;
}) {
  return (
    <span
      ref={probeRef}
      aria-hidden="true"
      className="mm-page-tabs__more-button mm-page-tabs__probe"
      data-label="More"
    >
      <NavIconChevronDown className="mm-page-tabs__more-chevron" />
    </span>
  );
}
