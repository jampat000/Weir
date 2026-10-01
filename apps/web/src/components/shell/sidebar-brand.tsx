import { Link } from "react-router-dom";

import { WeirMark } from "../brand/weir-logo";

type SidebarBrandProps = {
  productTitle: string;
  /** The computer Weir runs on, as Deluno shows its own under its name. */
  machineName: string | undefined;
  onNavigate: () => void;
};

/** What shows under the name until Weir has said which computer it is on. */
const NO_MACHINE = "Media cleaner";

/**
 * The mark on its dark tile, with the name and the computer Weir runs on. The tile is all that shows when the menu
 * is icons.
 */
export function SidebarBrand({
  productTitle,
  machineName,
  onNavigate,
}: SidebarBrandProps) {
  const machine = machineName?.trim() || undefined;
  return (
    <Link
      to="/"
      className="mm-sidebar-brand"
      aria-label={`${productTitle} home`}
      onClick={onNavigate}
    >
      <span className="mm-sidebar-brand__tile" aria-hidden="true">
        <WeirMark />
      </span>
      <span className="mm-sidebar-brand__words">
        <span className="mm-sidebar-brand__name">Weir</span>
        <span className="mm-sidebar-brand__tagline" title={machine}>
          {machine ?? NO_MACHINE}
        </span>
      </span>
    </Link>
  );
}
