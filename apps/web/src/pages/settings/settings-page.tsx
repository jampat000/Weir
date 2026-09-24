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
import { LibrariesTab } from "./tabs/libraries/libraries-tab";
import { CleanupTab } from "./tabs/cleanup/cleanup-tab";
import { PerformanceTab } from "./tabs/performance/performance-tab";
import { RulesTab } from "./tabs/rules/rules-tab";
import { ScheduleTab } from "./tabs/schedule/schedule-tab";

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

/**
 * Settings: how Weir treats your media, in the order someone sets Weir up in: where the media is, what
 * to keep, who to tell, then how hard to work and when. Weir itself (what it runs, what it keeps, who
 * can sign in) is the System screen, so a page about your library never sits beside one about backups.
 */
export function SettingsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  // The address is the tab, so Back, Forward and the side menu move between tabs too.
  const tab = normalizeSettingsTab(searchParams.get("tab"));

  function setSettingsTab(nextTab: TabId): void {
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
          <PerformanceTab />
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
