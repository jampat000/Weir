import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";

import { PageLoading } from "../../components/shared/page-loading";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceTabList,
  type WorkspaceTabOption,
} from "../../components/shared/workspace-shell";
import { ConfirmDialog } from "../../components/ui/confirm-dialog";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../lib/api/error-guards";
import { canEdit } from "../../lib/auth/can-edit";
import { useMeQuery } from "../../lib/auth/queries";
import { useAppSettingsQuery } from "../../lib/settings/queries";
import { AboutTab } from "./tabs/about/about-tab";
import { BackupsTab } from "./tabs/backups/backups-tab";
import { LogsTab } from "./tabs/logs/logs-tab";
import { SecurityTab } from "./tabs/security/security-tab";
import { useSystemSettingsForm } from "./use-system-settings-form";
import { useUnsavedChangesGuard } from "./use-unsaved-changes-guard";

type TabId = "about" | "backups" | "security" | "logs";

/** What it is first, then what it keeps, who can sign in, and last the record of what it did. */
const SYSTEM_TABS: readonly WorkspaceTabOption<TabId>[] = [
  { id: "about", label: "About" },
  { id: "backups", label: "Backups" },
  { id: "security", label: "Security" },
  { id: "logs", label: "Logs" },
];

/** A tab name from the address, including older names, which land on the tab that took them in. */
function systemTabFrom(candidate: string | null | undefined): TabId {
  switch ((candidate || "").trim().toLowerCase()) {
    case "backups":
    case "backup":
      return "backups";
    case "security":
      return "security";
    case "logs":
    case "history":
      return "logs";
    default:
      return "about";
  }
}

function SettingsLoadProblem({ error }: { error: Error }) {
  return (
    <div className="mm-page" data-testid="suite-settings-page">
      <header className="mm-page__intro">
        <h1 className="mm-page__title">Settings</h1>
        <p className="mm-page__lead">
          {isLikelyNetworkFailure(error)
            ? "Could not reach the Weir API. Check that the backend is running."
            : isHttpErrorFromApi(error)
              ? "The server refused this request. Sign in again, then try back here."
              : "Something went wrong loading settings."}
        </p>
      </header>
    </div>
  );
}

/** System: Weir itself. What it is running, what it keeps, and who can sign in. */
export function SystemPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const me = useMeQuery();
  const settingsQ = useAppSettingsQuery();
  const form = useSystemSettingsForm(settingsQ.data);
  const blocker = useUnsavedChangesGuard(form.isDirty);
  const [tab, setTab] = useState<TabId>(() =>
    systemTabFrom(searchParams.get("tab")),
  );
  const editable = canEdit(me.data?.role);

  // Back, Forward and the side menu change the address without remounting the page.
  useEffect(() => {
    setTab(systemTabFrom(searchParams.get("tab")));
  }, [searchParams]);

  function selectTab(nextTab: TabId): void {
    setTab(nextTab);
    const nextParams = new URLSearchParams(searchParams);
    // About is where System opens, so it needs no tab in the address.
    if (nextTab === "about") nextParams.delete("tab");
    else nextParams.set("tab", nextTab);
    for (const name of ["show", "status", "path"]) nextParams.delete(name);
    setSearchParams(nextParams);
  }

  if (settingsQ.isPending || me.isPending) {
    return <PageLoading label="Loading settings" />;
  }
  if (settingsQ.isError) {
    return <SettingsLoadProblem error={settingsQ.error} />;
  }
  const settings = settingsQ.data;

  return (
    <WorkspacePage
      title="System"
      dataTestId="suite-system-page"
      description="Weir itself: what it is running, what it keeps, and who can sign in."
    >
      <WorkspaceTabList
        tabs={SYSTEM_TABS}
        activeId={tab}
        onSelect={selectTab}
        ariaLabel="System sections"
        idPrefix="system-tab"
        panelId="system-panel"
        dataTestId="system-section-tabs"
      />
      <WorkspacePanel id="system-panel" labelledBy={`system-tab-${tab}`}>
        {tab === "about" ? (
          <AboutTab settings={settings} />
        ) : tab === "backups" ? (
          <BackupsTab form={form} editable={editable} settings={settings} />
        ) : tab === "security" ? (
          <div className="mm-quiet-stack">
            <SecurityTab />
          </div>
        ) : (
          <LogsTab
            form={form}
            editable={editable}
            savedLogDays={settings.log_retention_days}
          />
        )}
      </WorkspacePanel>
      {blocker.state === "blocked" ? (
        <ConfirmDialog
          title="Leave without saving?"
          description="You have unsaved changes."
          confirmLabel="Leave without saving"
          cancelLabel="Stay here"
          testId="unsaved-changes-dialog"
          onCancel={() => blocker.reset()}
          onConfirm={() => blocker.proceed()}
        />
      ) : null}
    </WorkspacePage>
  );
}
