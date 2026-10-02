import type { ReactNode } from "react";

import { useMediaQuery } from "../../lib/ui/use-media-query";
import { PageTabs, type PageTabOption } from "./page-tabs";
import { ShellHeaderTabs } from "./shell-header-context";

type PageToolbarProps<Id extends string> = {
  tabs: readonly PageTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  ariaLabel: string;
  idPrefix: string;
  panelId: string;
  /** The page's own buttons, in a row of their own under the title; narrow, at the right of the tab row. */
  actions?: ReactNode;
  /** Names the tab list. */
  dataTestId?: string;
};

/** The room the header needs to hold a title and its tabs on one line; narrower, the tabs go in the page's own row. */
const TABS_ON_TITLE_LINE = "(min-width: 1024px)";

/** The landmark around a page's tab row, named apart from the tab list inside it (#697). */
const TAB_ROW_LANDMARK_LABEL = "Page tabs";

/**
 * A page's tabs and its own buttons. Where the header has the room, the tabs sit on its title line, right after
 * the title, and the buttons keep a row under it that exists only while a tab has one. Narrower, the tabs leave
 * the header for the first row of the page, 40px, tabs at the left and buttons at the right, the chosen tab's
 * underline on the row's bottom line. The header names the page, so there is no title here.
 */
export function PageToolbar<Id extends string>({
  actions,
  dataTestId,
  ...tabs
}: PageToolbarProps<Id>) {
  const onTitleLine = useMediaQuery(TABS_ON_TITLE_LINE);

  if (onTitleLine) {
    return (
      <>
        <ShellHeaderTabs>
          <PageTabs {...tabs} placement="title" dataTestId={dataTestId} />
        </ShellHeaderTabs>
        {actions ? (
          <div className="mm-page-toolbar mm-page-toolbar--buttons">
            <div className="mm-page-toolbar__actions">{actions}</div>
          </div>
        ) : null}
      </>
    );
  }

  return (
    <div className="mm-page-toolbar">
      <nav
        className="mm-page-toolbar__tabs"
        aria-label={TAB_ROW_LANDMARK_LABEL}
      >
        <PageTabs {...tabs} placement="row" dataTestId={dataTestId} />
      </nav>
      {actions ? (
        <div className="mm-page-toolbar__actions">{actions}</div>
      ) : null}
    </div>
  );
}
