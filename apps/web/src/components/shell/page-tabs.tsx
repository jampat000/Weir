import type { KeyboardEvent } from "react";

export type PageTabOption<Id extends string> = Readonly<{
  id: Id;
  label: string;
}>;

type PageTabsProps<Id extends string> = {
  tabs: readonly PageTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  /** Names the tab list for a screen reader: "System sections". */
  ariaLabel: string;
  /** Each tab's element id is `${idPrefix}-${tab.id}`, for the panel to name itself by. */
  idPrefix: string;
  /** The panel the tabs switch, when one element holds it. */
  panelId?: string;
  /**
   * Where the tabs sit: "title" on the header's title line, right after the title; "row" in the toolbar
   * row under it.
   */
  placement: "title" | "row";
  dataTestId?: string;
};

/** Where each arrow key takes focus from tab `index` of `count`, wrapping at the ends. */
function tabIndexAfterKey(
  key: string,
  index: number,
  count: number,
): number | null {
  switch (key) {
    case "ArrowRight":
      return (index + 1) % count;
    case "ArrowLeft":
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
 * Weir's one tab style: plain 14px text 18px apart, the chosen tab in the accent colour with a 2px
 * underline, and the same weight in every state so the row never shifts as you move along it. It has the
 * keyboard behaviour of a tab list: Tab reaches the chosen tab only, and the arrow keys, Home and End move
 * to another tab and choose it.
 */
export function PageTabs<Id extends string>({
  tabs,
  activeId,
  onSelect,
  ariaLabel,
  idPrefix,
  panelId,
  placement,
  dataTestId,
}: PageTabsProps<Id>) {
  // Tab lands on the chosen tab; the first one stands in if nothing is chosen yet.
  const tabStop = tabs.some((tab) => tab.id === activeId)
    ? activeId
    : tabs[0]?.id;
  const choose = (event: KeyboardEvent<HTMLButtonElement>, index: number) => {
    const next = tabIndexAfterKey(event.key, index, tabs.length);
    if (next === null) return;
    event.preventDefault();
    onSelect(tabs[next].id);
    document.getElementById(`${idPrefix}-${tabs[next].id}`)?.focus();
  };

  return (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={`mm-page-tabs mm-page-tabs--${placement}`}
      data-testid={dataTestId}
    >
      {tabs.map(({ id, label }, index) => (
        <button
          key={id}
          type="button"
          role="tab"
          id={`${idPrefix}-${id}`}
          aria-controls={panelId}
          aria-selected={activeId === id}
          tabIndex={id === tabStop ? 0 : -1}
          className="mm-page-tabs__tab"
          onClick={() => onSelect(id)}
          onKeyDown={(event) => choose(event, index)}
        >
          {label}
        </button>
      ))}
    </div>
  );
}
