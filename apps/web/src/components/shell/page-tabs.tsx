import { useEffect, useRef, type KeyboardEvent } from "react";

import { PageTabsMore, PageTabsMoreProbe } from "./page-tabs-more";
import { usePageTabsFit } from "./use-page-tabs-fit";

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
   * Where the tabs sit: "title" on the header's title line, right after the title, where tabs that do not
   * fit fold into a More menu; "row" in the toolbar row under it.
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
 *
 * On the title line the tabs take the room the header gives them. When they do not all fit, the ones that
 * do not fold into a More menu, and the chosen tab always keeps a place in the row.
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
  const selectedIndex = tabs.findIndex((tab) => tab.id === activeId);
  const folds = placement === "title";
  const { rootRef, layerRef, probeRef, fit } = usePageTabsFit({
    labels: tabs.map((tab) => tab.label),
    selectedIndex,
    enabled: folds,
  });

  // A tab chosen while it is folded has no button to focus until it takes its place in the row.
  const focusWhenShown = useRef<string | null>(null);
  useEffect(() => {
    const pending = focusWhenShown.current;
    focusWhenShown.current = null;
    if (pending) document.getElementById(pending)?.focus();
  }, [activeId]);

  const tabElementId = (id: Id) => `${idPrefix}-${id}`;
  const chooseFolded = (id: Id) => {
    focusWhenShown.current = tabElementId(id);
    onSelect(id);
  };

  // Tab lands on the chosen tab; the first one stands in if nothing is chosen yet.
  const tabStop = selectedIndex >= 0 ? activeId : tabs[0]?.id;
  const onTabKeyDown = (
    event: KeyboardEvent<HTMLButtonElement>,
    index: number,
  ) => {
    const next = tabIndexAfterKey(event.key, index, tabs.length);
    if (next === null) return;
    event.preventDefault();
    const { id } = tabs[next];
    onSelect(id);
    const button = document.getElementById(tabElementId(id));
    if (button) button.focus();
    else focusWhenShown.current = tabElementId(id);
  };

  const tabList = (
    <div
      role="tablist"
      aria-label={ariaLabel}
      className={`mm-page-tabs mm-page-tabs--${placement}`}
      data-testid={dataTestId}
    >
      {fit.visible.map((index) => {
        const { id, label } = tabs[index];
        return (
          <button
            key={id}
            type="button"
            role="tab"
            id={tabElementId(id)}
            aria-controls={panelId}
            aria-selected={activeId === id}
            tabIndex={id === tabStop ? 0 : -1}
            className="mm-page-tabs__tab"
            onClick={() => onSelect(id)}
            onKeyDown={(event) => onTabKeyDown(event, index)}
          >
            {label}
          </button>
        );
      })}
    </div>
  );
  if (!folds) return tabList;

  return (
    <div className="mm-page-tabs-fit" ref={rootRef}>
      <div className="mm-page-tabs-fit__row">
        {tabList}
        {fit.folded.length > 0 ? (
          <PageTabsMore
            menuLabel={`More ${ariaLabel.toLowerCase()}`}
            tabs={fit.folded.map((index) => tabs[index])}
            onChoose={chooseFolded}
          />
        ) : null}
      </div>
      {/* Twins of every tab and of More, drawn out of sight to be measured. The words come from an attribute, so
          the page holds no second copy of them. */}
      <div ref={layerRef} aria-hidden="true" className="mm-page-tabs__measure">
        {tabs.map(({ id, label }) => (
          <span key={id} className="mm-page-tabs__tab" data-label={label} />
        ))}
      </div>
      <PageTabsMoreProbe probeRef={probeRef} />
    </div>
  );
}
