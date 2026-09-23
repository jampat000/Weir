import { useQueryClient } from "@tanstack/react-query";
import type { ChangeEvent } from "react";
import { useEffect, useState } from "react";
import { useBlocker, useSearchParams } from "react-router-dom";
import { PageLoading } from "../../components/shared/page-loading";
import {
  WorkspacePage,
  WorkspacePanel,
  WorkspaceTabList,
  type WorkspaceTabOption,
} from "../../components/shared/workspace-shell";
import {
  isHttpErrorFromApi,
  isLikelyNetworkFailure,
} from "../../lib/api/error-guards";
import { useMeQuery } from "../../lib/auth/queries";
import { CURATED_TIMEZONE_ID_SET } from "../../lib/suite/timezone-options";
import {
  suiteConfigurationBackupsQueryKey,
  useSuiteConfigurationBackupsQuery,
  useSuiteOperationalHistoryResetMutation,
  useSuiteSettingsQuery,
  useSuiteSettingsSaveMutation,
  useSuiteUpdateStatusQuery,
} from "../../lib/suite/queries";
import type { SuiteSettingsPutBody } from "../../lib/suite/types";
import {
  fetchConfigurationBundle,
  fetchStoredConfigurationBackupBlob,
  putConfigurationBundle,
  type ConfigurationBundle,
} from "../../lib/suite/suite-settings-api";
import { SHOW_SUPPORT_CARD } from "../../lib/support";
import {
  SettingsHistoryRetentionSection,
  SettingsInstanceSection,
} from "../settings/settings-general-tab";
import { SettingsBackupTab } from "../settings/settings-backup-tab";
import { SettingsUpgradeTab } from "../settings/settings-upgrade-tab";
import { SettingsSecurityTab } from "../settings/settings-security-tab";
import { SettingsLogsTab } from "../settings/settings-logs-tab";
import { SettingsSupportTab } from "../settings/settings-support-tab";
import { ActivityPage } from "../activity/activity-page";
import { ProcessingFilesSection } from "../processing/processing-files-section";
import { ProcessingJobsInspectionSection } from "../processing/processing-jobs-inspection-section";
import { ProcessingRuntimeFactsSection } from "../processing/processing-maintenance-section";

function canEditSuiteGlobal(role: string | undefined): boolean {
  return role === "operator" || role === "admin";
}

/**
 * The seven Settings tabs since 3.2. Weir is the whole app now, so everything set up once lives
 * here, including what used to be the configuration tabs on Processing
 * (docs/exec-plans/active/live-and-library.md). James chose tabs across the top over a second
 * side menu ("I dont like 2 side menus", 2026-09-22).
 */
type TabId = "instance" | "backups" | "security" | "history";

/**
 * The order someone actually sets Weir up in (James, 23 Sep 2026): say where the media is, say what to
 * keep, say who to tell, then tune how hard it works. Alerts and System come after, because Weir runs
 * without either being touched, and History is a record rather than a setting, so it sits last.
 *
 * General is gone: it was six unrelated jobs behind two different Save buttons. Its halves went to the
 * thing each is about — the time zone and the wizard to System, retention and clearing to History.
 */
const SYSTEM_TABS: readonly WorkspaceTabOption<TabId>[] = [
  { id: "instance", label: "This instance" },
  { id: "backups", label: "Backups" },
  { id: "security", label: "Security" },
  { id: "history", label: "History and logs" },
];

/** A tab name from the address, including the 3.1 names, which land on the tab that took them in. */
function normalizeSystemTab(candidate: string | null | undefined): TabId {
  switch ((candidate || "").trim().toLowerCase()) {
    case "backups":
    case "backup":
      return "backups";
    case "security":
      return "security";
    case "history":
    case "logs":
      return "history";
    default:
      return "instance";
  }
}

/** What History and logs shows: one "Show" choice beside the list, never a second row of tabs. */
type HistoryView = "activity" | "log" | "jobs" | "downloads";

const HISTORY_VIEWS: { id: HistoryView; label: string }[] = [
  { id: "activity", label: "Activity" },
  { id: "downloads", label: "Downloads" },
  { id: "jobs", label: "Jobs" },
  { id: "log", label: "Server log" },
];

function normalizeHistoryView(candidate: string | null): HistoryView {
  return HISTORY_VIEWS.some((view) => view.id === candidate)
    ? (candidate as HistoryView)
    : "activity";
}

/** Settings: everything set up once, in seven tabs. */
export function SystemPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const queryClient = useQueryClient();
  const me = useMeQuery();
  const settingsQ = useSuiteSettingsQuery();
  const save = useSuiteSettingsSaveMutation();
  const resetHistory = useSuiteOperationalHistoryResetMutation();

  const showSupport = SHOW_SUPPORT_CARD;
  const [tab, setTab] = useState<TabId>(() =>
    normalizeSystemTab(searchParams.get("tab")),
  );
  const historyView = normalizeHistoryView(searchParams.get("show"));

  function setSystemTab(nextTab: TabId): void {
    setTab(nextTab);
    const nextParams = new URLSearchParams(searchParams);
    // This instance is where System opens, so it needs no tab in the address.
    if (nextTab === "instance") {
      nextParams.delete("tab");
    } else {
      nextParams.set("tab", nextTab);
    }
    for (const name of ["show", "status", "path"]) nextParams.delete(name);
    setSearchParams(nextParams);
  }

  function setHistoryView(next: HistoryView): void {
    const nextParams = new URLSearchParams(searchParams);
    nextParams.set("tab", "history");
    if (next === "activity") nextParams.delete("show");
    else nextParams.set("show", next);
    for (const name of ["status", "path"]) nextParams.delete(name);
    setSearchParams(nextParams);
  }
  const [appTimezone, setAppTimezone] = useState<string | null>(null);
  const [logRetentionDaysDraft, setLogRetentionDaysDraft] = useState<
    string | null
  >(null);
  const [activityRetentionDaysDraft, setActivityRetentionDaysDraft] = useState<
    string | null
  >(null);
  const [backupBusy, setBackupBusy] = useState(false);
  const [backupMsg, setBackupMsg] = useState<string | null>(null);
  const [backupErr, setBackupErr] = useState<string | null>(null);
  const [resetHistoryMsg, setResetHistoryMsg] = useState<string | null>(null);
  const [resetHistoryConfirm, setResetHistoryConfirm] = useState("");
  const [configurationBackupEnabled, setConfigurationBackupEnabled] =
    useState(false);
  const [
    configurationBackupIntervalHours,
    setConfigurationBackupIntervalHours,
  ] = useState(24);
  const [
    configurationBackupPreferredTime,
    setConfigurationBackupPreferredTime,
  ] = useState("02:00");
  const [lastSuiteSaveTarget, setLastSuiteSaveTarget] = useState<
    "timezone" | "logs" | "backup" | null
  >(null);

  useEffect(() => {
    if (!settingsQ.data) {
      return;
    }
    const fromServer = settingsQ.data.app_timezone || "";
    setAppTimezone(CURATED_TIMEZONE_ID_SET.has(fromServer) ? fromServer : null);
    setLogRetentionDaysDraft(null);
    setActivityRetentionDaysDraft(null);
    setConfigurationBackupEnabled(
      Boolean(settingsQ.data.configuration_backup_enabled),
    );
    setConfigurationBackupIntervalHours(
      Number.isFinite(
        Number(settingsQ.data.configuration_backup_interval_hours),
      )
        ? Number(settingsQ.data.configuration_backup_interval_hours)
        : 24,
    );
    setConfigurationBackupPreferredTime(
      (settingsQ.data.configuration_backup_preferred_time || "02:00").trim() ||
        "02:00",
    );
  }, [settingsQ.data]);

  const editable = canEditSuiteGlobal(me.data?.role);
  const backupsQ = useSuiteConfigurationBackupsQuery(
    editable && tab === "instance" && Boolean(settingsQ.data),
  );
  const updateStatusQ = useSuiteUpdateStatusQuery(
    tab === "instance" && Boolean(settingsQ.data),
  );

  const serverCuratedTimezone =
    settingsQ.data &&
    CURATED_TIMEZONE_ID_SET.has(settingsQ.data.app_timezone || "")
      ? settingsQ.data.app_timezone
      : null;

  const timezoneDirty =
    settingsQ.data !== undefined && appTimezone !== serverCuratedTimezone;

  const logsDirty =
    settingsQ.data !== undefined &&
    logRetentionDaysDraft !== null &&
    logRetentionDaysDraft !== String(settingsQ.data.log_retention_days);
  const activityRetentionDirty =
    settingsQ.data !== undefined &&
    activityRetentionDaysDraft !== null &&
    activityRetentionDaysDraft !==
      String(settingsQ.data.activity_retention_days ?? "");
  const backupScheduleDirty =
    settingsQ.data !== undefined &&
    (configurationBackupEnabled !==
      Boolean(settingsQ.data.configuration_backup_enabled) ||
      configurationBackupIntervalHours !==
        Number(settingsQ.data.configuration_backup_interval_hours || 24) ||
      configurationBackupPreferredTime !==
        ((
          settingsQ.data.configuration_backup_preferred_time || "02:00"
        ).trim() || "02:00"));
  const isDirty =
    timezoneDirty || logsDirty || activityRetentionDirty || backupScheduleDirty;
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) =>
      isDirty && currentLocation.pathname !== nextLocation.pathname,
  );
  useEffect(() => {
    if (blocker.state !== "blocked") return;
    if (window.confirm("You have unsaved changes. Leave without saving?")) {
      blocker.proceed();
    } else {
      blocker.reset();
    }
  }, [blocker]);
  useEffect(() => {
    if (!isDirty) return;
    const handler = (e: BeforeUnloadEvent) => {
      e.preventDefault();
    };
    window.addEventListener("beforeunload", handler);
    return () => window.removeEventListener("beforeunload", handler);
  }, [isDirty]);

  useEffect(() => {
    setTab(normalizeSystemTab(searchParams.get("tab")));
  }, [searchParams]);

  const loadingAny = settingsQ.isPending || me.isPending;

  async function handleDownloadConfiguration() {
    setBackupErr(null);
    setBackupMsg(null);
    setBackupBusy(true);
    try {
      const bundle = await fetchConfigurationBundle();
      const blob = new Blob([JSON.stringify(bundle, null, 2)], {
        type: "application/json",
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `weir-configuration-${new Date().toISOString().slice(0, 10)}.json`;
      a.click();
      URL.revokeObjectURL(url);
      setBackupMsg("Download started.");
    } catch (e) {
      setBackupErr(e instanceof Error ? e.message : "Could not export.");
    } finally {
      setBackupBusy(false);
    }
  }

  async function handleRestoreFileChange(event: ChangeEvent<HTMLInputElement>) {
    const input = event.target;
    const file = input.files?.[0];
    input.value = "";
    if (!file) {
      return;
    }
    setBackupErr(null);
    setBackupMsg(null);
    try {
      const text = await file.text();
      const parsed = JSON.parse(text) as unknown;
      if (typeof parsed !== "object" || parsed === null) {
        setBackupErr("This file is not valid JSON.");
        return;
      }
      const bundle = parsed as ConfigurationBundle;
      // Only check that this looks like a bundle at all. Which format versions are
      // supported is the server's to decide, and it answers 400 with a reason for one it
      // cannot take (ConfigurationBundleStore.FormatVersion). Restating the number here is
      // what broke this: the client insisted on 1 while the server had moved to 4, so every
      // real export was rejected before it was ever sent.
      if (typeof bundle.format_version !== "number") {
        setBackupErr("This file is not a Weir configuration export.");
        return;
      }
      if (
        !window.confirm(
          "Replace the settings on this server from this file? This cannot be undone.",
        )
      ) {
        return;
      }
      setBackupBusy(true);
      await putConfigurationBundle(bundle);
      await queryClient.invalidateQueries();
      const refreshed = await settingsQ.refetch();
      if (refreshed.data) {
        const tz = refreshed.data.app_timezone || "";
        setAppTimezone(CURATED_TIMEZONE_ID_SET.has(tz) ? tz : null);
        setLogRetentionDaysDraft(null);
        setActivityRetentionDaysDraft(null);
      }
      setBackupMsg("Configuration restored.");
    } catch (e) {
      setBackupErr(e instanceof Error ? e.message : "Could not restore.");
    } finally {
      setBackupBusy(false);
    }
  }

  if (loadingAny) {
    return <PageLoading label="Loading settings" />;
  }

  if (settingsQ.isError) {
    const err = settingsQ.error;
    return (
      <div className="mm-page" data-testid="suite-settings-page">
        <header className="mm-page__intro">
          <h1 className="mm-page__title">Settings</h1>
          <p className="mm-page__lead">
            {isLikelyNetworkFailure(err)
              ? "Could not reach the Weir API. Check that the backend is running."
              : isHttpErrorFromApi(err)
                ? "The server refused this request. Sign in again, then try back here."
                : "Something went wrong loading settings."}
          </p>
        </header>
      </div>
    );
  }

  if (!settingsQ.data) {
    return null;
  }

  const normalizedLogRetentionDraft =
    logRetentionDaysDraft !== null
      ? logRetentionDaysDraft
      : String(settingsQ.data.log_retention_days);
  const finalizeLogRetentionDays = (): number => {
    const raw = normalizedLogRetentionDraft.trim();
    if (raw === "") {
      return 30;
    }
    const n = Number(raw);
    if (!Number.isFinite(n)) {
      return settingsQ.data.log_retention_days;
    }
    return Math.min(Math.max(Math.trunc(n), 1), 3650);
  };

  const serverActivityRetention = settingsQ.data.activity_retention_days;
  const normalizedActivityRetentionDraft =
    activityRetentionDaysDraft !== null
      ? activityRetentionDaysDraft
      : String(serverActivityRetention ?? "");
  /** 0 keeps Activity history until it is cleared; blank or invalid keeps the saved value. */
  const finalizeActivityRetentionDays = (): number | undefined => {
    const raw = normalizedActivityRetentionDraft.trim();
    const n = Number(raw);
    if (raw === "" || !Number.isFinite(n)) {
      return serverActivityRetention;
    }
    return Math.min(Math.max(Math.trunc(n), 0), 3650);
  };

  const buildSuitePutBody = (): SuiteSettingsPutBody => {
    const d = settingsQ.data;
    const name = (d.product_display_name || "Weir").trim() || "Weir";
    const tz = (appTimezone ?? d.app_timezone ?? "UTC").trim() || "UTC";
    const retention = Math.min(
      3650,
      Math.max(1, Math.trunc(Number(finalizeLogRetentionDays()))),
    );
    const body: SuiteSettingsPutBody = {
      product_display_name: name,
      signed_in_home_notice: d.signed_in_home_notice,
      setup_wizard_state: d.setup_wizard_state,
      app_timezone: tz,
      log_retention_days: Number.isFinite(retention)
        ? retention
        : d.log_retention_days,

      ...(finalizeActivityRetentionDays() !== undefined
        ? { activity_retention_days: finalizeActivityRetentionDays() }
        : {}),
      configuration_backup_enabled: Boolean(configurationBackupEnabled),
      configuration_backup_interval_hours: Math.min(
        720,
        Math.max(1, Math.trunc(Number(configurationBackupIntervalHours))),
      ),
      configuration_backup_preferred_time:
        configurationBackupPreferredTime.trim() || "02:00",
    };
    return body;
  };

  async function handleSaveTimezone() {
    if (!settingsQ.data) {
      return;
    }
    setLastSuiteSaveTarget("timezone");
    save.reset();
    try {
      await save.mutateAsync(buildSuitePutBody());
      setLastSuiteSaveTarget(null);
    } catch {
      /* surfaced via save.isError */
    }
  }

  async function handleSaveLogs() {
    if (!settingsQ.data) {
      return;
    }
    setLastSuiteSaveTarget("logs");
    save.reset();
    try {
      await save.mutateAsync(buildSuitePutBody());
      setLastSuiteSaveTarget(null);
    } catch {
      /* surfaced via save.isError */
    }
  }

  async function handleSaveBackupSchedule() {
    if (!settingsQ.data) {
      return;
    }
    setBackupErr(null);
    setBackupMsg(null);
    setLastSuiteSaveTarget("backup");
    save.reset();
    try {
      await save.mutateAsync(buildSuitePutBody());
      setLastSuiteSaveTarget(null);
      setBackupMsg("Backup schedule saved.");
      await queryClient.invalidateQueries({
        queryKey: suiteConfigurationBackupsQueryKey,
      });
    } catch (e) {
      setBackupErr(
        e instanceof Error ? e.message : "Could not save backup schedule.",
      );
    }
  }

  async function handleDownloadStoredBackup(id: number, fileLabel: string) {
    setBackupErr(null);
    setBackupMsg(null);
    setBackupBusy(true);
    try {
      const blob = await fetchStoredConfigurationBackupBlob(id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = fileLabel.replace(/[^\w.-]+/g, "_").slice(0, 120);
      a.click();
      URL.revokeObjectURL(url);
      setBackupMsg("Download started.");
    } catch (e) {
      setBackupErr(
        e instanceof Error ? e.message : "Could not download snapshot.",
      );
    } finally {
      setBackupBusy(false);
    }
  }

  async function handleResetOperationalHistory() {
    setResetHistoryMsg(null);
    try {
      const result = await resetHistory.mutateAsync(resetHistoryConfirm);
      setResetHistoryConfirm("");
      setResetHistoryMsg(
        `History reset. Removed ${result.total_deleted} old history ${result.total_deleted === 1 ? "item" : "items"}.`,
      );
    } catch {
      /* surfaced below */
    }
  }

  return (
    <WorkspacePage
      title="System"
      dataTestId="suite-system-page"
      description="Weir itself: what it is running, what it keeps, and who can sign in."
    >
      <WorkspaceTabList
        tabs={SYSTEM_TABS}
        activeId={tab}
        onSelect={setSystemTab}
        ariaLabel="System sections"
        idPrefix="system-tab"
        panelId="system-panel"
        dataTestId="system-section-tabs"
      />
      <WorkspacePanel id="system-panel" labelledBy={`system-tab-${tab}`}>
        {tab === "instance" ? (
          <div className="mm-quiet-stack mm-quiet-stack--columns">
            {/* What it is, before anything you can change about it. */}
            <ProcessingRuntimeFactsSection />
            <SettingsInstanceSection
              editable={editable}
              settingsData={settingsQ.data}
              save={save}
              appTimezone={appTimezone}
              setAppTimezone={setAppTimezone}
              timezoneDirty={timezoneDirty}
              lastSuiteSaveTarget={lastSuiteSaveTarget}
              onSaveTimezone={() => void handleSaveTimezone()}
            />
            <SettingsUpgradeTab updateStatusQ={updateStatusQ} />
            {showSupport ? <SettingsSupportTab /> : null}
          </div>
        ) : tab === "backups" ? (
          <div className="mm-quiet-stack mm-quiet-stack--columns">
            <SettingsBackupTab
              editable={editable}
              settingsData={settingsQ.data}
              save={save}
              backupScheduleDirty={backupScheduleDirty}
              lastSuiteSaveTarget={lastSuiteSaveTarget}
              configurationBackupEnabled={configurationBackupEnabled}
              setConfigurationBackupEnabled={setConfigurationBackupEnabled}
              configurationBackupIntervalHours={
                configurationBackupIntervalHours
              }
              setConfigurationBackupIntervalHours={
                setConfigurationBackupIntervalHours
              }
              configurationBackupPreferredTime={
                configurationBackupPreferredTime
              }
              setConfigurationBackupPreferredTime={
                setConfigurationBackupPreferredTime
              }
              backupsQ={backupsQ}
              backupBusy={backupBusy}
              backupMsg={backupMsg}
              backupErr={backupErr}
              onSaveBackupSchedule={() => void handleSaveBackupSchedule()}
              onDownloadConfiguration={() => void handleDownloadConfiguration()}
              onRestoreFileChange={(e) => void handleRestoreFileChange(e)}
              onDownloadStoredBackup={(id, fileLabel) =>
                void handleDownloadStoredBackup(id, fileLabel)
              }
            />
          </div>
        ) : tab === "security" ? (
          <div className="mm-quiet-stack mm-quiet-stack--columns">
            <SettingsSecurityTab />
          </div>
        ) : (
          <div className="mm-quiet-stack" data-testid="settings-history">
            <label className="mm-history-show">
              <span>Show</span>
              <select
                className="mm-input"
                data-testid="settings-history-show"
                value={historyView}
                onChange={(e) =>
                  setHistoryView(normalizeHistoryView(e.target.value))
                }
              >
                {HISTORY_VIEWS.map((view) => (
                  <option key={view.id} value={view.id}>
                    {view.label}
                  </option>
                ))}
              </select>
            </label>
            {historyView === "activity" ? (
              <ActivityPage embedded />
            ) : historyView === "downloads" ? (
              <ProcessingFilesSection />
            ) : historyView === "jobs" ? (
              <ProcessingJobsInspectionSection />
            ) : (
              <SettingsLogsTab />
            )}
            {/* How long what you are looking at is kept, and how to empty it — beside the thing it governs. */}
            <SettingsHistoryRetentionSection
              editable={editable}
              settingsData={settingsQ.data}
              save={save}
              setLogRetentionDaysDraft={setLogRetentionDaysDraft}
              normalizedLogRetentionDraft={normalizedLogRetentionDraft}
              finalizeLogRetentionDays={finalizeLogRetentionDays}
              logsDirty={logsDirty || activityRetentionDirty}
              normalizedActivityRetentionDraft={
                normalizedActivityRetentionDraft
              }
              setActivityRetentionDaysDraft={setActivityRetentionDaysDraft}
              finalizeActivityRetentionDays={finalizeActivityRetentionDays}
              lastSuiteSaveTarget={lastSuiteSaveTarget}
              resetHistoryConfirm={resetHistoryConfirm}
              setResetHistoryConfirm={setResetHistoryConfirm}
              resetHistory={resetHistory}
              resetHistoryMsg={resetHistoryMsg}
              onSaveLogs={() => void handleSaveLogs()}
              onResetOperationalHistory={() =>
                void handleResetOperationalHistory()
              }
            />
          </div>
        )}
      </WorkspacePanel>
    </WorkspacePage>
  );
}
