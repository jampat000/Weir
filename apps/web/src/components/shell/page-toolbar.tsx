import type { ReactNode } from "react";

import { useMediaQuery } from "../../lib/ui/use-media-query";
import { PageTabs, type PageTabOption } from "./page-tabs";
import { ShellHeaderButtons, ShellHeaderTabs } from "./shell-header-context";

type PageToolbarProps<Id extends string> = {
  tabs: readonly PageTabOption<Id>[];
  activeId: Id;
  onSelect: (id: Id) => void;
  ariaLabel: string;
  idPrefix: string;
  panelId: string;
  /** The page's own buttons: on the header's title line, at the right; narrow, at the right of the tab row. */
  actions?: ReactNode;
  /** Names the tab list. */
  dataTestId?: string;
};

/** The room the header needs to hold a title and its tabs on one line; narrower, the tabs go in the page's own row. */
const TABS_ON_TITLE_LINE = "(min-width: 1024px)";

/** The landmark around a page's tab row, named apart from the tab list inside it (#697). */
const TAB_ROW_LANDMARK_LABEL = "Page tabs";

/**
 * A page's tabs and its own buttons. Where the header has the room, both go on its title line: the tabs right after
 * the title, the buttons at the right, just before Pause, so the page has no row of its own. Narrower, they leave the
 * header for the first row of the page, 40px, tabs at the left and buttons at the right, the chosen tab's underline on
 * the row's bottom line. The header names the page, so there is no title here.
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
        <ShellHeaderButtons>{actions}</ShellHeaderButtons>
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
