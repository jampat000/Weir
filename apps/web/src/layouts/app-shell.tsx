import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import { useEffect, useState, useSyncExternalStore } from "react";
import { BrandHeaderLink } from "../components/brand/brand-header-link";
import {
  NavIconChevronLeft,
  NavIconChevronRight,
  NavIconLibrary,
  NavIconHistory,
  NavIconLive,
  NavIconSettings,
  NavIconSystem,
  NavIconSignOut,
} from "../components/shell/nav-icons";
import { useLogoutMutation } from "../lib/auth/queries";
import { useProcessingFilesAtOnceQuery } from "../lib/processing/queries";
import { useAppSettingsQuery } from "../lib/settings/queries";
import { useSystemReadinessQuery } from "../lib/system/readiness-queries";

// Between phone width (a drawer below 921px) and 1400px the side menu shrinks to icons by itself, so
// Live keeps its lanes; a click on Collapse or Expand overrides it until the page is reloaded.
const LAPTOP_WIDTH = "(min-width: 921px) and (max-width: 1400px)";

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

function sidebarNavClass({ isActive }: { isActive: boolean }) {
  return isActive ? "mm-sidebar-link active" : "mm-sidebar-link";
}

export function AppShell() {
  const navigate = useNavigate();
  const location = useLocation();
  const logout = useLogoutMutation();
  const suite = useAppSettingsQuery();
  const readiness = useSystemReadinessQuery();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [collapsedChoice, setCollapsedChoice] = useState<boolean | null>(null);
  const laptop = useMediaQuery(LAPTOP_WIDTH);
  const sidebarCollapsed = collapsedChoice ?? laptop;
  // How many files are being written right now, beside Live wherever you are in the app.
  const working = useProcessingFilesAtOnceQuery().data?.running ?? 0;
  const productTitle =
    (suite.data?.product_display_name ?? "Weir").trim() || "Weir";
  const appVersion = readiness.data?.version;

  useEffect(() => {
    window.scrollTo(0, 0);
  }, [location.pathname, location.search]);

  const handleSignOut = () => {
    logout.mutate(undefined, {
      onSettled: () => {
        void navigate("/login", { replace: true });
      },
    });
    void navigate("/login", { replace: true });
  };

  return (
    <div className="mm-app-layout" data-testid="shell-ready">
      <aside
        id="mm-primary-sidebar"
        className={`mm-sidebar${sidebarOpen ? " mm-sidebar--open" : ""}${sidebarCollapsed ? " mm-sidebar--collapsed" : ""}`}
        // Named for the app it belongs to. It used to be "Product", from when the sidebar listed
        // the separate products of a suite; there is one product now and a landmark announced as
        // "Product, complementary" told a screen-reader user nothing about where they were.
        aria-label={productTitle}
      >
        <button
          type="button"
          className="mm-sidebar-collapse"
          data-testid="sidebar-collapse"
          aria-label={
            sidebarCollapsed ? "Expand navigation" : "Collapse navigation"
          }
          aria-expanded={!sidebarCollapsed}
          onClick={() => setCollapsedChoice(!sidebarCollapsed)}
        >
          {/* The chevron says it on its own; aria-label carries the words for anyone who needs
              them (James, 23 Sep 2026). */}
          <span className="mm-sidebar-collapse__icon" aria-hidden="true">
            {sidebarCollapsed ? (
              <NavIconChevronRight />
            ) : (
              <NavIconChevronLeft />
            )}
          </span>
        </button>
        <div className="mm-sidebar-inner">
          <BrandHeaderLink to="/" productTitle={productTitle} />
          {/* Five places since 3.2: what Weir is doing now, every file it has worked on, the files
              already imported, how Weir treats your media, and Weir itself. */}
          <nav className="mm-sidebar-nav" aria-label="Primary">
            <NavLink
              to="/"
              end
              className={sidebarNavClass}
              title="Processing"
              aria-label={
                working > 0 ? `Processing, ${working} working` : undefined
              }
              onClick={() => setSidebarOpen(false)}
            >
              <span className="mm-sidebar-link-icon" aria-hidden="true">
                <NavIconLive />
              </span>
              <span className="mm-sidebar-link-label">Processing</span>
              {working > 0 ? (
                <span
                  className="mm-sidebar-link-badge"
                  data-testid="nav-processing-working"
                >
                  <i className="mm-live-pulse" aria-hidden="true" />
                  <span className="mm-sidebar-link-badge__text">
                    {working} working
                  </span>
                </span>
              ) : null}
            </NavLink>
            <NavLink
              to="/history"
              className={sidebarNavClass}
              title="History"
              onClick={() => setSidebarOpen(false)}
            >
              <span className="mm-sidebar-link-icon" aria-hidden="true">
                <NavIconHistory />
              </span>
              <span className="mm-sidebar-link-label">History</span>
            </NavLink>
            <NavLink
              to="/library"
              className={sidebarNavClass}
              title="Library"
              onClick={() => setSidebarOpen(false)}
            >
              <span className="mm-sidebar-link-icon" aria-hidden="true">
                <NavIconLibrary />
              </span>
              <span className="mm-sidebar-link-label">Library</span>
            </NavLink>
            <NavLink
              to="/settings"
              className={sidebarNavClass}
              title="Settings"
              onClick={() => setSidebarOpen(false)}
            >
              <span className="mm-sidebar-link-icon" aria-hidden="true">
                <NavIconSettings />
              </span>
              <span className="mm-sidebar-link-label">Settings</span>
            </NavLink>
            <NavLink
              to="/system"
              className={sidebarNavClass}
              title="System"
              onClick={() => setSidebarOpen(false)}
            >
              <span className="mm-sidebar-link-icon" aria-hidden="true">
                <NavIconSystem />
              </span>
              <span className="mm-sidebar-link-label">System</span>
            </NavLink>
          </nav>
          <div className="mm-sidebar-footer">
            <div className="mm-sidebar-footer-panel">
              <div className="mm-sidebar-meta">{productTitle}</div>
              <div
                className="mm-sidebar-version"
                title="Installed Weir version reported by the running server"
              >
                {appVersion ? `Version ${appVersion}` : "Version checking..."}
              </div>
              <button
                type="button"
                data-testid="sign-out"
                className="mm-sidebar-signout"
                disabled={logout.isPending}
                onClick={handleSignOut}
                title={sidebarCollapsed ? "Sign out" : undefined}
              >
                <span className="mm-sidebar-signout__icon" aria-hidden="true">
                  <NavIconSignOut />
                </span>
                <span className="mm-sidebar-signout__label">
                  {logout.isPending ? "Signing out…" : "Sign out"}
                </span>
              </button>
            </div>
          </div>
        </div>
      </aside>
      {sidebarOpen ? (
        <button
          type="button"
          className="mm-sidebar-backdrop"
          aria-label="Close navigation"
          onClick={() => setSidebarOpen(false)}
        />
      ) : null}
      <main className="mm-main" id="mm-main-content" tabIndex={-1}>
        <div className="mm-main-inner">
          {/* Phones only (hidden on wider screens by CSS): the menu button that opens the side
              menu. Pause and the theme switch are in each page's title row. */}
          <div className="mm-shell-toolbar">
            <button
              type="button"
              className="mm-shell-menu-toggle"
              data-testid="shell-nav-toggle"
              aria-controls="mm-primary-sidebar"
              aria-expanded={sidebarOpen}
              onClick={() => setSidebarOpen((value) => !value)}
            >
              <span aria-hidden="true">☰</span>
              <span>Menu</span>
            </button>
          </div>
          <Outlet />
        </div>
      </main>
    </div>
  );
}
