import { Link } from "react-router-dom";

import { WeirMark } from "../brand/weir-logo";

type SidebarBrandProps = {
  productTitle: string;
  onNavigate: () => void;
};

/** The mark on its dark tile, with the name and what Weir is. The tile is all that shows when the menu is icons. */
export function SidebarBrand({ productTitle, onNavigate }: SidebarBrandProps) {
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
        <span className="mm-sidebar-brand__tagline">Media cleaner</span>
      </span>
    </Link>
  );
}
