import type { KeyboardEvent, ReactNode } from "react";
import { PageHeader } from "../shell/page-header";
import {
  mmModuleTabBlurbBandClass,
  mmModuleTabBlurbTextClass,
} from "../../lib/ui/mm-module-tab-blurb";
import { mmSectionTabClass } from "../../lib/ui/mm-control-roles";

type WorkspacePageProps = {
  title: string;
  description: ReactNode;
  children: ReactNode;
  dataTestId?: string;
};

/** A page with a tab row: the shared title row (Pause and the theme switch on the right), then the tabs. */
export function WorkspacePage({
  title,
  description,
  children,
  dataTestId,
}: WorkspacePageProps) {
  return (
    <div className="mm-page mm-workspace-page" data-testid={dataTestId}>
      <PageHeader title={title} lead={description} />
      {children}
    </div>
  );
}

export type WorkspaceTabOption<Id extends string> = Readonly<{
  id: Id;
  label: string;
}>;

type WorkspaceTabListProps<Id extends string> = {
  tabs: readonly WorkspaceTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  ariaLabel: string;
  idPrefix: string;
  panelId: string;
  dataTestId?: string;
};

/** The landmark around a page's tab row, named apart from the tab list inside it (#697). */
const TAB_ROW_LANDMARK_LABEL = "Page tabs";

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
 * A row of tabs with the keyboard behaviour of a tab list: Tab reaches the chosen tab only, and the
 * arrow keys, Home and End move to another tab and choose it.
 */
export function WorkspaceTabList<Id extends string>({
  tabs,
  activeId,
  onSelect,
  ariaLabel,
  idPrefix,
  panelId,
  dataTestId,
}: WorkspaceTabListProps<Id>) {
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
    <nav
      className="mm-workspace-tabs"
      aria-label={TAB_ROW_LANDMARK_LABEL}
      data-testid={dataTestId}
    >
      <div
        role="tablist"
        aria-label={ariaLabel}
        className="mm-workspace-tabs__list"
      >
        {tabs.map(({ id, label }, index) => {
          const selected = activeId === id;
          return (
            <button
              key={id}
              type="button"
              role="tab"
              id={`${idPrefix}-${id}`}
              aria-controls={panelId}
              aria-selected={selected}
              tabIndex={id === tabStop ? 0 : -1}
              className={`${mmSectionTabClass(selected)} mm-workspace-tabs__button`}
              onClick={() => onSelect(id)}
              onKeyDown={(event) => choose(event, index)}
            >
              {label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}

type WorkspacePanelProps = {
  id: string;
  labelledBy: string;
  context?: ReactNode;
  children: ReactNode;
  contextTestId?: string;
};

export function WorkspacePanel({
  id,
  labelledBy,
  context,
  children,
  contextTestId,
}: WorkspacePanelProps) {
  return (
    <section
      id={id}
      className="mm-workspace-panel mm-bubble-stack"
      role="tabpanel"
      aria-labelledby={labelledBy}
    >
      {context !== undefined ? (
        <div className={mmModuleTabBlurbBandClass} data-testid={contextTestId}>
          <p className={mmModuleTabBlurbTextClass}>{context}</p>
        </div>
      ) : null}
      <div className="mm-workspace-panel__content mm-bubble-stack">
        {children}
      </div>
    </section>
  );
}
