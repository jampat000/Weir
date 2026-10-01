import { useRef, useState, useSyncExternalStore } from "react";
import { useNavigate } from "react-router-dom";

import { useLogoutMutation, useMeQuery } from "../../lib/auth/queries";
import { useNeedsYouCount } from "../../lib/processing/needs-you-count";
import { useSystemReadinessQuery } from "../../lib/system/readiness-queries";
import { useModalFocus } from "../../lib/ui/use-modal-focus";
import { useWorkingCount } from "../../pages/processing/working-count";
import { NavIconChevronLeft, NavIconChevronRight } from "./nav-icons";
import { SidebarBrand } from "./sidebar-brand";
import { SidebarNav } from "./sidebar-nav";
import { SidebarUser } from "./sidebar-user";

// Between phone width (a drawer below 921px) and 1100px the side menu shrinks to icons by itself, so
// the Processing lanes keep their room; a click on Collapse or Expand overrides it until a reload.
// At 1100px and wider, including the common 1280px, every label stays readable (#697).
const LAPTOP_WIDTH = "(min-width: 921px) and (max-width: 1099px)";

function useMediaQuery(query: string): boolean {
  return useSyncExternalStore(
    (onChange) => {
      if (typeof window.matchMedia !== "function") return () => undefined;
      const list = window.matchMedia(query);
      list.addEventListener("change", onChange);
      return () => list.removeEventListener("change", onChange);
    },
    () =>
      typeof window.matchMedia === "function" &&
      window.matchMedia(query).matches,
    () => false,
  );
}

type AppSidebarProps = {
  /** The name the sidebar landmark answers to, so a screen reader says where the user is. */
  productTitle: string;
  /** On a phone the menu is a drawer, and this says whether it is open. */
  drawerOpen: boolean;
  onCloseDrawer: () => void;
};

/**
 * The side menu: brand, the groups of places, and who is signed in. Wide, it sits beside the page; between
 * phone and laptop width it shrinks to icons; on a phone it is a drawer that takes focus when it opens.
 */
export function AppSidebar({
  productTitle,
  drawerOpen,
  onCloseDrawer,
}: AppSidebarProps) {
  const navigate = useNavigate();
  const me = useMeQuery();
  const logout = useLogoutMutation();
  const readiness = useSystemReadinessQuery();
  const working = useWorkingCount();
  const needsYou = useNeedsYouCount();
  const firstPlace = useRef<HTMLAnchorElement>(null);
  const drawer = useModalFocus<HTMLElement>({
    open: drawerOpen,
    onClose: onCloseDrawer,
    initialFocus: firstPlace,
  });
  const [collapsedChoice, setCollapsedChoice] = useState<boolean | null>(null);
  const laptop = useMediaQuery(LAPTOP_WIDTH);
  const collapsed = collapsedChoice ?? laptop;

  const signOut = () => {
    // The sign-in page shows at once; the session ends on the server behind it.
    logout.mutate();
    void navigate("/login", { replace: true });
  };

  return (
    <aside
      id="mm-primary-sidebar"
      className={`mm-sidebar${drawerOpen ? " mm-sidebar--open" : ""}${collapsed ? " mm-sidebar--collapsed" : ""}`}
      aria-label={productTitle}
      ref={drawer}
    >
      <button
        type="button"
        className="mm-sidebar-collapse"
        data-testid="sidebar-collapse"
        aria-label={collapsed ? "Expand navigation" : "Collapse navigation"}
        aria-expanded={!collapsed}
        onClick={() => setCollapsedChoice(!collapsed)}
      >
        {collapsed ? <NavIconChevronRight /> : <NavIconChevronLeft />}
      </button>
      <div className="mm-sidebar-inner">
        <SidebarBrand productTitle={productTitle} onNavigate={onCloseDrawer} />
        <SidebarNav
          badges={{ working, "needs-you": needsYou }}
          firstLinkRef={firstPlace}
          onNavigate={onCloseDrawer}
        />
        <SidebarUser
          username={me.data?.username}
          accountRole={me.data?.role}
          machineName={readiness.data?.machine_name}
          version={readiness.data?.version}
          signingOut={logout.isPending}
          onSignOut={signOut}
          onNavigate={onCloseDrawer}
        />
      </div>
    </aside>
  );
}
