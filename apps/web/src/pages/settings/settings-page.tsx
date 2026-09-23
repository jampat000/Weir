import { useEffect, useState } from "react";
import { Navigate, useSearchParams } from "react-router-dom";
import { systemAddressForSettingsTab } from "../../app/legacy-redirects";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceTabList,
  type WorkspaceTabOption,
} from "../../components/shared/workspace-shell";
import { MediaManagersTab } from "./tabs/media-managers/media-managers-tab";
import { AlertsTab } from "./tabs/alerts/alerts-tab";
import { DirectPlaySection } from "./tabs/performance/direct-play-section";
import { LibrariesTab } from "./tabs/libraries/libraries-tab";
import { CleanupTab } from "./tabs/cleanup-tab";
import { ProcessSettingsSection } from "./tabs/performance/process-settings-section";
import { RulesTab } from "./tabs/rules/rules-tab";
import { ScheduleTab } from "./tabs/schedule/schedule-tab";

/**
 * Settings: how Weir treats your media. Where it lives, what to keep, who to tell about it, how hard
 * to work and when, and what to say when something needs you.
 *
 * Weir itself — what it is running, what it keeps, who can sign in — is the System screen beside this
 * one in the menu (James, 23 Sep 2026). Splitting them turned one row of nine tabs into two short
 * rows, and stopped a page about your library sitting next to a page about restoring a backup.
 *
 * The order is the order someone sets Weir up in: say where the media is, say what to keep, say who
 * to tell, then tune how hard it works and when. James chose tabs across the top over a second side
 * menu ("I dont like 2 side menus", 2026-09-22).
 */
type TabId =
  | "libraries"
  | "rules"
  | "media-managers"
  | "performance"
  | "cleanup"
  | "schedule"
  | "alerts";

const SETTINGS_TABS: readonly WorkspaceTabOption<TabId>[] = [
  { id: "libraries", label: "Libraries" },
  { id: "rules", label: "Rules" },
  { id: "media-managers", label: "Media managers" },
  { id: "performance", label: "Performance" },
  { id: "cleanup", label: "Cleanup" },
  { id: "schedule", label: "Schedule" },
  { id: "alerts", label: "Alerts" },
];

/** A tab name from the address, including the names earlier versions used. */
function normalizeSettingsTab(candidate: string | null | undefined): TabId {
  switch ((candidate || "").trim().toLowerCase()) {
    case "rules":
    case "audio-subtitles":
      return "rules";
    case "media-managers":
      return "media-managers";
    case "performance":
    case "running":
    case "processing":
      return "performance";
    case "cleanup":
    case "housekeeping":
    case "maintenance":
      return "cleanup";
    case "schedule":
    case "schedules":
      return "schedule";
    case "alerts":
    case "notifications":
      return "alerts";
    default:
      return "libraries";
  }
}

export function SettingsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [tab, setTab] = useState<TabId>(() =>
    normalizeSettingsTab(searchParams.get("tab")),
  );

  // Back, Forward and the side menu change the address without remounting the page.
  useEffect(() => {
    setTab(normalizeSettingsTab(searchParams.get("tab")));
  }, [searchParams]);

  function setSettingsTab(nextTab: TabId): void {
    setTab(nextTab);
    const nextParams = new URLSearchParams(searchParams);
    // Libraries is where Settings opens, so it needs no tab in the address.
    if (nextTab === "libraries") {
      nextParams.delete("tab");
    } else {
      nextParams.set("tab", nextTab);
    }
    for (const name of ["show", "status", "path"]) nextParams.delete(name);
    setSearchParams(nextParams);
  }

  const movedToSystem = systemAddressForSettingsTab(searchParams.get("tab"));
  if (movedToSystem) {
    return <Navigate to={movedToSystem} replace />;
  }

  return (
    <WorkspacePage
      title="Settings"
      dataTestId="suite-settings-page"
      description="How Weir treats your media. Work down the tabs and it is set up."
    >
      <WorkspaceTabList
        tabs={SETTINGS_TABS}
        activeId={tab}
        onSelect={setSettingsTab}
        ariaLabel="Settings sections"
        idPrefix="settings-tab"
        panelId="settings-panel"
        dataTestId="settings-section-tabs"
      />
      <WorkspacePanel id="settings-panel" labelledBy={`settings-tab-${tab}`}>
        {tab === "libraries" ? (
          <LibrariesTab />
        ) : tab === "rules" ? (
          <RulesTab />
        ) : tab === "media-managers" ? (
          <MediaManagersTab />
        ) : tab === "performance" ? (
          <div className="mm-quiet-stack">
            <ProcessSettingsSection />
            <DirectPlaySection />
          </div>
        ) : tab === "cleanup" ? (
          <CleanupTab />
        ) : tab === "schedule" ? (
          <ScheduleTab />
        ) : (
          <AlertsTab />
        )}
      </WorkspacePanel>
    </WorkspacePage>
  );
}
