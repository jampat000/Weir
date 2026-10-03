import { useSetPageEyebrow } from "./shell-header-context";

type PageHeaderProps = {
  /**
   * The line above the shell's title, for a page whose line depends on what it has loaded. A page that
   * leaves it out keeps the menu's own line for the page.
   */
  eyebrow?: string;
};

/**
 * What a page adds to the shell's header, which owns the title, Pause and the theme switch: its own
 * eyebrow. A page's own controls go on the header's title line (see ShellHeaderSlot, ShellHeaderTabs and
 * ShellHeaderButtons), not in a row under it.
 */
export function PageHeader({ eyebrow }: PageHeaderProps) {
  useSetPageEyebrow(eyebrow);
  return null;
}
