import type { ReactNode } from "react";
import { PauseControl } from "./pause-control";
import { ThemeToggle } from "./theme-toggle";

type PageHeaderProps = {
  title: ReactNode;
  /** One sentence under the title row. Optional: Processing and Library say it with their content. */
  lead?: ReactNode;
  /** Sits right after the title on the same line, e.g. Library's library picker. */
  titleAfter?: ReactNode;
  /** A second line beside the lead, right-aligned, e.g. the library's scan status. */
  aside?: ReactNode;
  dataTestId?: string;
};

/**
 * Every page's title row: the title on the left, Pause and the theme switch on the right, so a
 * page starts with what it is rather than with a toolbar.
 */
export function PageHeader({
  title,
  lead,
  titleAfter,
  aside,
  dataTestId,
}: PageHeaderProps) {
  return (
    <header className="mm-page-head" data-testid={dataTestId}>
      <div className="mm-page-head__row">
        <div className="mm-page-head__title-wrap">
          <h1 className="mm-page-head__title">{title}</h1>
          {titleAfter}
        </div>
        <div className="mm-page-head__actions">
          <PauseControl />
          <ThemeToggle />
        </div>
      </div>
      {lead || aside ? (
        <div className="mm-page-head__sub">
          {lead ? <p className="mm-page-head__lead">{lead}</p> : <span />}
          {aside ? <div className="mm-page-head__aside">{aside}</div> : null}
        </div>
      ) : null}
    </header>
  );
}
