import { NavLink, Outlet, useNavigate } from "react-router-dom";
import { BrandHeaderLink } from "../components/brand/brand-header-link";
import {
  NavIconActivity,
  NavIconDashboard,
  NavIconFetcher,
  NavIconRefiner,
  NavIconSettings,
  NavIconSubtitles,
  NavIconTrimmer,
} from "../components/shell/nav-icons";
import { WEB_APP_VERSION } from "../lib/app-meta";
import { useLogoutMutation } from "../lib/auth/queries";

function sidebarNavClass({ isActive }: { isActive: boolean }) {
  return isActive ? "mm-sidebar-link active" : "mm-sidebar-link";
}

export function AppShell() {
  const navigate = useNavigate();
  const logout = useLogoutMutation();

  return (
    <div className="mm-app-layout">
      <aside className="mm-sidebar" aria-label="Product">
        <BrandHeaderLink to="/app" />
        <nav className="mm-sidebar-nav" aria-label="Primary">
          <p className="mm-sidebar-section-label">Overview</p>
          <NavLink to="/app" end className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconDashboard />
            </span>
            <span className="mm-sidebar-link-label">Dashboard</span>
          </NavLink>
          <NavLink to="/app/activity" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconActivity />
            </span>
            <span className="mm-sidebar-link-label">Activity</span>
          </NavLink>

          <p className="mm-sidebar-section-label">Suite</p>
          <NavLink to="/app/fetcher" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconFetcher />
            </span>
            <span className="mm-sidebar-link-label">Fetcher</span>
          </NavLink>
          <NavLink to="/app/refiner" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconRefiner />
            </span>
            <span className="mm-sidebar-link-label">Refiner</span>
          </NavLink>
          <NavLink to="/app/trimmer" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconTrimmer />
            </span>
            <span className="mm-sidebar-link-label">Trimmer</span>
          </NavLink>
          {/* TODO: Nav label "Subtitles" is temporary until the module name is finalized. */}
          <NavLink to="/app/subtitles" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconSubtitles />
            </span>
            <span className="mm-sidebar-link-label">Subtitles</span>
          </NavLink>

          <p className="mm-sidebar-section-label">System</p>
          <NavLink to="/app/settings" className={sidebarNavClass}>
            <span className="mm-sidebar-link-icon" aria-hidden="true">
              <NavIconSettings />
            </span>
            <span className="mm-sidebar-link-label">Settings</span>
          </NavLink>
        </nav>
        <div className="mm-sidebar-footer">
          <div className="mm-sidebar-footer-panel">
            <div className="mm-sidebar-meta">MediaMop</div>
            <div className="mm-sidebar-version" title="Web shell version (package.json)">
              Web {WEB_APP_VERSION}
            </div>
            <button
              type="button"
              data-testid="sign-out"
              className="mm-sidebar-signout"
              disabled={logout.isPending}
              onClick={() => {
                void logout.mutateAsync().then(() => navigate("/login", { replace: true }));
              }}
            >
              {logout.isPending ? "Signing out…" : "Sign out"}
            </button>
          </div>
        </div>
      </aside>
      <main className="mm-main" id="mm-main-content" tabIndex={-1}>
        <div className="mm-main-inner">
          <Outlet />
        </div>
      </main>
    </div>
  );
}
