import type { ReactNode } from "react";

import { PageTabs, type PageTabOption } from "./page-tabs";

type PageToolbarProps<Id extends string> = {
  tabs: readonly PageTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  ariaLabel: string;
  idPrefix: string;
  panelId: string;
  /** The page's own buttons, at the right of the row. */
  actions?: ReactNode;
  dataTestId?: string;
};

/** The landmark around a page's tab row, named apart from the tab list inside it (#697). */
const TAB_ROW_LANDMARK_LABEL = "Page tabs";

/**
 * The first row of a page with tabs, under the shell's header: 40px, the tabs at the left starting under the
 * title's first letter, the page's own buttons at the right. The chosen tab's underline sits on the row's
 * bottom line. The header above names the page, so there is no title here.
 */
export function PageToolbar<Id extends string>({
  actions,
  dataTestId,
  ...tabs
}: PageToolbarProps<Id>) {
  return (
    <div className="mm-page-toolbar" data-testid={dataTestId}>
      <nav
        className="mm-page-toolbar__tabs"
        aria-label={TAB_ROW_LANDMARK_LABEL}
      >
        <PageTabs {...tabs} placement="row" />
      </nav>
      {actions ? (
        <div className="mm-page-toolbar__actions">{actions}</div>
      ) : null}
    </div>
  );
}
