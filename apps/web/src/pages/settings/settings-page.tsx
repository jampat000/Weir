import {
  Navigate,
  useBlocker,
  useSearchParams,
  type Location,
} from "react-router-dom";
import { systemAddressForSettingsTab } from "../../app/legacy-redirects";
import { WorkspacePage } from "../../components/shared/workspace-shell";
import {
  normalizeSettingsSection,
  settingsSection,
  settingsSectionFromSearch,
  type SettingsSectionId,
} from "../../lib/settings/settings-sections";
import { MediaManagersTab } from "./tabs/media-managers/media-managers-tab";
import { AlertsTab } from "./tabs/alerts/alerts-tab";
import { LibrariesTab } from "./tabs/libraries/libraries-tab";
import { CleanupTab } from "./tabs/cleanup/cleanup-tab";
import { PerformanceTab } from "./tabs/performance/performance-tab";
import { RulesTab } from "./tabs/rules/rules-tab";
import { ScheduleTab } from "./tabs/schedule/schedule-tab";
import {
  LeaveWithoutSavingDialog,
  UnsavedChangesScope,
  useUnsavedChangesRegistry,
} from "./unsaved-changes";

/** Leaving Settings, or moving to another of its sections, would unmount the panel holding the edits. */
function leavesPanel(current: Location, next: Location): boolean {
  return (
    current.pathname !== next.pathname ||
    settingsSectionFromSearch(current.search) !==
      settingsSectionFromSearch(next.search)
  );
}

/**
 * Settings: how Weir treats your media, in the order someone sets Weir up in: where the media is, what
 * to keep, who to tell, then how hard to work and when. The side menu is the way between sections, and
 * the header names the one showing. Weir itself (what it runs, what it keeps, who can sign in) is the
 * System screen, so a page about your library never sits beside one about backups.
 */
export function SettingsPage() {
  const [searchParams] = useSearchParams();
  // The address is the section, so Back, Forward and the side menu all move between sections.
  const section = normalizeSettingsSection(searchParams.get("tab"));
  const { unsaved, register } = useUnsavedChangesRegistry();
  // Back, Forward and the side menu are all navigation, so one blocker asks for all of them.
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      unsaved !== null && leavesPanel(currentLocation, nextLocation),
  );

  const movedToSystem = systemAddressForSettingsTab(searchParams.get("tab"));
  if (movedToSystem) {
    return <Navigate to={movedToSystem} replace />;
  }

  return (
    <WorkspacePage dataTestId="suite-settings-page">
      <section
        className="mm-workspace-panel mm-bubble-stack"
        aria-label={settingsSection(section).label}
      >
        <div className="mm-workspace-panel__content mm-bubble-stack">
          <UnsavedChangesScope register={register}>
            <SettingsSectionPanel section={section} />
          </UnsavedChangesScope>
        </div>
      </section>
      {blocker.state === "blocked" && unsaved !== null ? (
        <LeaveWithoutSavingDialog
          thing={unsaved}
          onStay={() => blocker.reset()}
          onLeave={() => blocker.proceed()}
        />
      ) : null}
    </WorkspacePage>
  );
}

function SettingsSectionPanel({ section }: { section: SettingsSectionId }) {
  switch (section) {
    case "libraries":
      return <LibrariesTab />;
    case "rules":
      return <RulesTab />;
    case "media-managers":
      return <MediaManagersTab />;
    case "performance":
      return <PerformanceTab />;
    case "cleanup":
      return <CleanupTab />;
    case "schedule":
      return <ScheduleTab />;
    case "alerts":
      return <AlertsTab />;
  }
}
