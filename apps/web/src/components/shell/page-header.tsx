import type { ReactNode } from "react";

import { useSetPageEyebrow } from "./shell-header-context";

type PageHeaderProps = {
  /**
   * The line above the shell's title, for a page whose line depends on what it has loaded. A page that
   * leaves it out keeps the menu's own line for the page.
   */
  eyebrow?: string;
  /** Controls that have no room in the shell's header, in a row at the top of the page. */
  children?: ReactNode;
  dataTestId?: string;
};

/**
 * What a page adds to the shell's header, which owns the title, Pause and the theme switch: its own
 * eyebrow, and an optional row of controls at the top of the page.
 */
export function PageHeader({ eyebrow, children, dataTestId }: PageHeaderProps) {
  useSetPageEyebrow(eyebrow);
  if (!children) return null;
  return (
    <div className="mm-page-head" data-testid={dataTestId}>
      {children}
    </div>
  );
}
