import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import * as authApi from "../../lib/api/auth-api";
import type { CurrentSession, UserPublic } from "../../lib/api/types";
import { authKeys } from "../../lib/auth/query-keys";
import * as settingsApi from "../../lib/settings/settings-api";
import { settingsKeys } from "../../lib/settings/query-keys";
import type {
  ServerLogs,
  ServerMetrics,
  SecurityOverview,
  AppSettings,
  UpdateStatus,
} from "../../lib/settings/types";
import { SystemPage } from "./system-page";

const operatorMe: UserPublic = { id: 1, username: "alice", role: "operator" };
const viewerMe: UserPublic = { id: 2, username: "bob", role: "viewer" };

const minimalAppSettings: AppSettings = {
  product_display_name: "Weir",
  signed_in_home_notice: null,
  setup_wizard_state: "pending",
  app_timezone: "UTC",
  log_retention_days: 30,
  activity_retention_days: 90,
  configuration_backup_enabled: false,
  configuration_backup_interval_hours: 24,
  configuration_backup_preferred_time: "02:00",
  configuration_backup_last_run_at: null,
  updated_at: "2026-04-11T00:00:00Z",
};

const minimalUpdateStatus: UpdateStatus = {
  current_version: "1.0.0",
  install_type: "source",
  status: "up_to_date",
  summary: "This install is already on Weir 1.0.0.",
  latest_version: "1.0.0",
  latest_name: "Weir 1.0.0",
  published_at: null,
  release_url: "https://example.com/release",
  windows_installer_url: null,
  docker_image: null,
  docker_tag: null,
  docker_update_command: null,
  in_app_upgrade_supported: false,
  in_app_upgrade_summary: null,
};

const windowsUpdateAvailableStatus: UpdateStatus = {
  ...minimalUpdateStatus,
  current_version: "2.0.7",
  install_type: "windows",
  status: "update_available",
  summary: "Weir 2.0.8 is available.",
  latest_version: "2.0.8",
  latest_name: "Weir 2.0.8",
  windows_installer_url:
    "https://github.com/jampat000/Weir/releases/download/v2.0.8/Weir-win-Setup.exe",
  in_app_upgrade_supported: true,
  in_app_upgrade_summary:
    "Updates are managed by the Weir desktop app via Velopack.",
};

const minimalSecurity: SecurityOverview = {
  session_signing_configured: true,
  sign_in_cookie_https_mode: "auto",
  sign_in_cookie_https_plain:
    "Matched to each connection — on over HTTPS, off over plain HTTP on your network.",
  sign_in_cookie_same_site: "Lax (recommended for most setups)",
  standard_session_idle_timeout_plain: "14 days",
  standard_session_absolute_timeout_plain: "90 days",
  trusted_session_idle_timeout_plain: "60 days",
  trusted_session_absolute_timeout_plain: "365 days",
  extra_https_hardening_enabled: false,
  sign_in_attempt_limit: 30,
  sign_in_attempt_window_plain: "1 minute",
  first_time_setup_attempt_limit: 10,
  first_time_setup_attempt_window_plain: "1 hour",
  allowed_browser_origins_count: 1,
  restart_required_note:
    "These safety options are read when the app starts from the server configuration file. To change them, ask whoever runs the server to edit that file and restart the app.",
};

const minimalCurrentSession: CurrentSession = {
  session_id: "00000000-0000-0000-0000-000000000001",
  client_label: "Chrome on Windows",
  current: true,
  trusted_device: true,
  created_at: "2026-04-11T00:00:00Z",
  last_seen_at: "2026-04-11T00:05:00Z",
  absolute_expires_at: "2027-04-11T00:00:00Z",
  idle_timeout_minutes: 86400,
  absolute_timeout_days: 365,
};

const minimalLogs: ServerLogs = {
  items: [],
  total: 0,
  counts: {
    error: 0,
    warning: 0,
    information: 0,
  },
};

const minimalMetrics: ServerMetrics = {
  uptime_seconds: 3600,
  total_requests: 20,
  average_response_ms: 12,
  error_log_count: 0,
  status_counts: { "2xx": 18, "3xx": 0, "4xx": 2, "5xx": 0 },
  busiest_routes: [],
};

function wrap(ui: ReactNode, client: QueryClient) {
  const router = createMemoryRouter([{ path: "*", element: ui }]);
  return (
    <QueryClientProvider client={client}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  );
}

function renderSettings(
  me: UserPublic,
  overrides?: {
    updateStatus?: UpdateStatus;
    initialEntries?: string[];
    backupItems?: {
      id: number;
      created_at: string;
      size_bytes: number;
      file_name: string;
    }[];
  },
) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  qc.setQueryData(settingsKeys.app, minimalAppSettings);
  qc.setQueryData(settingsKeys.securityOverview, minimalSecurity);
  qc.setQueryData(authKeys.me, me);
  qc.setQueryData(authKeys.session, minimalCurrentSession);
  qc.setQueryData(settingsKeys.configurationBackups, {
    directory: "C:/Weir/backups/suite-configuration",
    items: overrides?.backupItems ?? [],
  });
  qc.setQueryData(
    settingsKeys.updateStatus,
    overrides?.updateStatus ?? minimalUpdateStatus,
  );
  qc.setQueryData(
    [
      ...settingsKeys.logs,
      {
        level: undefined,
        search: undefined,
        has_exception: undefined,
        limit: 250,
      },
    ],
    minimalLogs,
  );
  qc.setQueryData(settingsKeys.metrics, minimalMetrics);
  const router = createMemoryRouter(
    [{ path: "*", element: <SystemPage /> }],
    overrides?.initialEntries
      ? { initialEntries: overrides.initialEntries }
      : undefined,
  );
  return render(
    <QueryClientProvider client={qc}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

async function renderSettingsWithSupportConfig(
  me: UserPublic,
  supportConfig: {
    showCard: boolean;
    showPlaceholder: boolean;
    supportUrl: string | null;
  },
  overrides?: { updateStatus?: UpdateStatus },
) {
  vi.resetModules();
  vi.doMock("../../lib/support", () => ({
    SHOW_SUPPORT_CARD: supportConfig.showCard,
    SHOW_SUPPORT_URL_PLACEHOLDER: supportConfig.showPlaceholder,
    SUPPORT_URL: supportConfig.supportUrl,
  }));

  const { SystemPage: SettingsPageWithMockedSupport } =
    await import("./system-page");

  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false, staleTime: Infinity } },
  });
  qc.setQueryData(settingsKeys.app, minimalAppSettings);
  qc.setQueryData(settingsKeys.securityOverview, minimalSecurity);
  qc.setQueryData(authKeys.me, me);
  qc.setQueryData(authKeys.session, minimalCurrentSession);
  qc.setQueryData(settingsKeys.configurationBackups, {
    directory: "C:/Weir/backups/suite-configuration",
    items: [],
  });
  qc.setQueryData(
    settingsKeys.updateStatus,
    overrides?.updateStatus ?? minimalUpdateStatus,
  );
  qc.setQueryData(
    [
      ...settingsKeys.logs,
      {
        level: undefined,
        search: undefined,
        has_exception: undefined,
        limit: 250,
      },
    ],
    minimalLogs,
  );
  qc.setQueryData(settingsKeys.metrics, minimalMetrics);
  return render(wrap(<SettingsPageWithMockedSupport />, qc));
}

describe("SystemPage", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.doUnmock("../../lib/support");
    vi.resetModules();
  });

  it("does not mention Sonarr or Radarr on the System page", () => {
    const { container } = renderSettings(operatorMe, {
      initialEntries: ["/system"],
    });
    const t = (container.textContent ?? "").toLowerCase();
    expect(t).not.toContain("sonarr");
    expect(t).not.toContain("radarr");
    expect(screen.getByTestId("suite-system-page")).toBeTruthy();
    expect(screen.getByTestId("suite-settings-global")).toBeTruthy();
  });

  it("shows development support guidance in System without a button when URL is missing", () => {
    renderSettings(operatorMe, { initialEntries: ["/system?tab=backups"] });
    expect(
      screen.queryByTestId("suite-settings-support"),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "About" }));
    expect(screen.getByTestId("suite-settings-support")).toBeInTheDocument();
    expect(
      screen.getByText("Weir is free to use. Support is optional."),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "If Weir saves you time or keeps your downloads clean, you can support ongoing development.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByText(/Development note: set/i)).toBeInTheDocument();
    expect(screen.getByText("VITE_SUPPORT_URL")).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Support Weir" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText(/supporter licence/i)).not.toBeInTheDocument();
  });

  it("shows the Support settings destination and button when a valid support URL is configured", async () => {
    await renderSettingsWithSupportConfig(operatorMe, {
      showCard: true,
      showPlaceholder: false,
      supportUrl: "https://example.com/support",
    });

    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(screen.getByTestId("suite-settings-support")).toBeInTheDocument();
    expect(
      screen.getByText("Weir is free to use. Support is optional."),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "If Weir saves you time or keeps your downloads clean, you can support ongoing development.",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: "Support Weir →" }),
    ).toHaveAttribute("href", "https://example.com/support");
    expect(screen.queryByText("VITE_SUPPORT_URL")).not.toBeInTheDocument();
    expect(screen.queryByText(/supporter licence/i)).not.toBeInTheDocument();
    expect(screen.queryByText(/feature limits/i)).not.toBeInTheDocument();
  });

  it("hides the Support settings destination in production when the support URL is missing or invalid", async () => {
    await renderSettingsWithSupportConfig(operatorMe, {
      showCard: false,
      showPlaceholder: false,
      supportUrl: null,
    });

    expect(
      screen.queryByRole("tab", { name: "Support" }),
    ).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("tab", { name: "About" }));
    expect(
      screen.queryByTestId("suite-settings-support"),
    ).not.toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Support Weir" }),
    ).not.toBeInTheDocument();
    expect(screen.queryByText("VITE_SUPPORT_URL")).not.toBeInTheDocument();
    expect(screen.queryByText(/supporter licence/i)).not.toBeInTheDocument();
  });

  it("hides save for viewers", () => {
    renderSettings(viewerMe, { initialEntries: ["/system?tab=history"] });
    expect(screen.getByTestId("suite-settings-save-logs")).toBeDisabled();
  });

  it("shows configuration backup + export for operators", () => {
    renderSettings(operatorMe, { initialEntries: ["/system?tab=backups"] });
    expect(screen.getByTestId("suite-settings-backup-restore")).toBeTruthy();
    expect(
      screen.getByRole("button", { name: "Download configuration now" }),
    ).toBeEnabled();
    expect(
      screen.getByRole("button", { name: "Restore from file..." }),
    ).toBeEnabled();
    expect(
      screen.getByRole("button", { name: "Save backup schedule" }),
    ).toBeDisabled();
  });

  it("backs up now and shows the result beside the export buttons", async () => {
    const postNowSpy = vi
      .spyOn(settingsApi, "postConfigurationBackupNow")
      .mockResolvedValue({
        id: 9,
        created_at: "2026-04-12T00:00:00Z",
        size_bytes: 512,
        file_name: "weir-configuration-2026-04-12.json",
      });
    renderSettings(operatorMe, { initialEntries: ["/system?tab=backups"] });

    fireEvent.click(screen.getByRole("button", { name: "Back up now" }));

    await waitFor(() => {
      expect(postNowSpy).toHaveBeenCalledTimes(1);
    });
    expect(await screen.findByText("Backup created.")).toBeInTheDocument();
  });

  it("offers restoring a saved backup and confirms in Weir's own dialog, not window.confirm", async () => {
    const confirmSpy = vi.spyOn(window, "confirm");
    vi.spyOn(
      settingsApi,
      "fetchStoredConfigurationBackupBlob",
    ).mockResolvedValue(
      new Blob([JSON.stringify({ format_version: 4, suite_settings: {} })], {
        type: "application/json",
      }),
    );
    const putBundleSpy = vi
      .spyOn(settingsApi, "putConfigurationBundle")
      .mockResolvedValue({ format_version: 4 });
    renderSettings(operatorMe, {
      initialEntries: ["/system?tab=backups"],
      backupItems: [
        {
          id: 3,
          created_at: "2026-04-10T00:00:00Z",
          size_bytes: 2048,
          file_name: "weir-configuration-2026-04-10.json",
        },
      ],
    });

    fireEvent.click(
      screen.getByRole("button", { name: "Restore this backup →" }),
    );

    const dialog = await screen.findByTestId("restore-configuration-dialog");
    expect(confirmSpy).not.toHaveBeenCalled();
    fireEvent.click(
      within(dialog).getByRole("button", { name: "Replace settings" }),
    );

    await waitFor(() => {
      expect(putBundleSpy).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(
        screen.queryByTestId("restore-configuration-dialog"),
      ).not.toBeInTheDocument();
    });
  });

  it("shows a plain-language restore error for a file that is not a Weir backup, never raw format_version text", async () => {
    renderSettings(operatorMe, { initialEntries: ["/system?tab=backups"] });
    const fileInput = document.querySelector(
      'input[type="file"]',
    ) as HTMLInputElement;
    const badFile = new File(['{"hello":true}'], "not-a-backup.json", {
      type: "application/json",
    });

    fireEvent.change(fileInput, { target: { files: [badFile] } });

    expect(
      await screen.findByText("This file is not a Weir configuration export."),
    ).toBeInTheDocument();
    expect(screen.queryByText(/format_version/)).not.toBeInTheDocument();
    expect(
      screen.queryByTestId("restore-configuration-dialog"),
    ).not.toBeInTheDocument();
  });

  it("saves how long Activity history is kept, including 0 for until cleared", async () => {
    vi.spyOn(settingsApi, "fetchAppSettings").mockResolvedValue(
      minimalAppSettings,
    );
    const putAppSettingsSpy = vi
      .spyOn(settingsApi, "putAppSettings")
      .mockImplementation(async (body) => ({
        ...minimalAppSettings,
        activity_retention_days: body.activity_retention_days ?? 0,
      }));

    renderSettings(operatorMe, { initialEntries: ["/system?tab=history"] });
    const input = await screen.findByTestId(
      "suite-settings-activity-retention",
    );
    expect(input).toHaveValue(90);
    fireEvent.change(input, { target: { value: "0" } });
    fireEvent.click(screen.getByTestId("suite-settings-save-logs"));

    await waitFor(() => {
      expect(putAppSettingsSpy).toHaveBeenCalledTimes(1);
    });
    expect(putAppSettingsSpy.mock.calls[0]?.[0]).toMatchObject({
      activity_retention_days: 0,
      log_retention_days: 30,
    });
  });

  it("allows changing and saving backup schedule more than once", async () => {
    let currentSavedSettings: AppSettings = {
      ...minimalAppSettings,
      configuration_backup_enabled: true,
      configuration_backup_interval_hours: 24,
      configuration_backup_preferred_time: "02:00",
    };
    vi.spyOn(settingsApi, "fetchAppSettings").mockImplementation(
      async () => currentSavedSettings,
    );
    const putAppSettingsSpy = vi
      .spyOn(settingsApi, "putAppSettings")
      .mockImplementation(async (body) => {
        currentSavedSettings = {
          ...currentSavedSettings,
          configuration_backup_enabled:
            body.configuration_backup_enabled ?? false,
          configuration_backup_interval_hours:
            body.configuration_backup_interval_hours ?? 24,
          configuration_backup_preferred_time:
            body.configuration_backup_preferred_time ?? "02:00",
          updated_at: "2026-04-12T00:00:00Z",
        };
        return currentSavedSettings;
      });

    renderSettings(operatorMe, { initialEntries: ["/system?tab=backups"] });

    fireEvent.change(screen.getByLabelText("Minimum time between runs"), {
      target: { value: "12" },
    });
    fireEvent.change(screen.getByLabelText("Preferred backup time"), {
      target: { value: "03:30" },
    });
    fireEvent.click(
      screen.getByRole("button", { name: "Save backup schedule" }),
    );

    await waitFor(() => {
      expect(putAppSettingsSpy).toHaveBeenCalledTimes(1);
    });
    await waitFor(() => {
      expect(
        screen.getByRole("button", { name: "Save backup schedule" }),
      ).toBeDisabled();
    });

    fireEvent.change(screen.getByLabelText("Minimum time between runs"), {
      target: { value: "24" },
    });
    fireEvent.change(screen.getByLabelText("Preferred backup time"), {
      target: { value: "04:15" },
    });

    const saveButton = screen.getByRole("button", {
      name: "Save backup schedule",
    });
    expect(saveButton).toBeEnabled();
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(putAppSettingsSpy).toHaveBeenCalledTimes(2);
    });
    expect(putAppSettingsSpy.mock.calls[1]?.[0]).toMatchObject({
      configuration_backup_interval_hours: 24,
      configuration_backup_preferred_time: "04:15",
    });
  });

  it("hides configuration backup for viewers", () => {
    renderSettings(viewerMe);
    fireEvent.click(screen.getByRole("tab", { name: "About" }));
    expect(
      screen.queryByTestId("suite-settings-backup-restore"),
    ).not.toBeInTheDocument();
  });

  it("opens on About, and each tab holds one job", () => {
    renderSettings(operatorMe);
    // System opens with what the instance is, before anything you can change about it.
    expect(screen.getByRole("tab", { name: "About" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    // The time zone moved to Settings › Schedule, beside the times it governs.
    expect(screen.queryByText("Time zone")).not.toBeInTheDocument();
    expect(screen.getByText("Setup wizard")).toBeInTheDocument();
    // Housekeeping is a file-processing job, so it lives under Settings, not here.
    expect(screen.queryByText("Housekeeping")).not.toBeInTheDocument();
    expect(
      screen.queryByTestId("suite-settings-backup-restore"),
    ).not.toBeInTheDocument();

    fireEvent.click(screen.getByRole("tab", { name: "Backups" }));
    expect(screen.getByTestId("suite-settings-backup-tab")).toBeInTheDocument();
    expect(screen.queryByText("Time zone")).not.toBeInTheDocument();

    // How long history is kept sits with the history it governs.
    fireEvent.click(screen.getByRole("tab", { name: "Logs" }));
    expect(screen.getByText("System log retention (days)")).toBeInTheDocument();
    expect(
      screen.getByText("Keep Activity history for (days)"),
    ).toBeInTheDocument();
    fireEvent.change(screen.getByTestId("settings-history-show"), {
      target: { value: "log" },
    });
    expect(screen.getByText("Search logs")).toBeInTheDocument();
    // Retention stays in view whichever of History's lists you are reading: it governs all of them.
    expect(screen.getByText("System log retention (days)")).toBeInTheDocument();
  });

  it("opens System when an old address asks for the upgrade tab", () => {
    renderSettings(operatorMe, {
      updateStatus: windowsUpdateAvailableStatus,
      initialEntries: ["/system?tab=upgrade"],
    });

    expect(screen.getByRole("tab", { name: "About" })).toHaveAttribute(
      "aria-selected",
      "true",
    );
    expect(
      screen.getByTestId("suite-settings-upgrade-tab"),
    ).toBeInTheDocument();
    // System is also where this instance's own facts live now, so they are here too.
    expect(screen.getByTestId("suite-settings-global")).toBeInTheDocument();
  });

  it("shows Checking... and disables button when refetch is in flight", async () => {
    vi.spyOn(settingsApi, "fetchUpdateStatus").mockImplementation(
      () => new Promise(() => {}),
    );
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    });
    qc.setQueryData(settingsKeys.app, minimalAppSettings);
    qc.setQueryData(settingsKeys.securityOverview, minimalSecurity);
    qc.setQueryData(authKeys.me, operatorMe);
    qc.setQueryData(authKeys.session, minimalCurrentSession);
    qc.setQueryData(settingsKeys.configurationBackups, {
      directory: "C:/Weir/backups/suite-configuration",
      items: [],
    });
    qc.setQueryData(settingsKeys.updateStatus, windowsUpdateAvailableStatus);
    qc.setQueryData(
      [
        ...settingsKeys.logs,
        {
          level: undefined,
          search: undefined,
          has_exception: undefined,
          limit: 250,
        },
      ],
      minimalLogs,
    );
    qc.setQueryData(settingsKeys.metrics, minimalMetrics);

    render(wrap(<SystemPage />, qc));
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    // staleTime: Infinity means the pre-seeded data is fresh — the link reads "Check again →"
    expect(screen.getByRole("button", { name: "Check again →" })).toBeEnabled();

    // Trigger a manual refetch (mock never resolves)
    fireEvent.click(screen.getByRole("button", { name: "Check again →" }));

    await waitFor(() => {
      expect(
        screen.getByRole("button", { name: "Checking..." }),
      ).toBeDisabled();
    });
    expect(
      screen.queryByRole("button", { name: "Check again →" }),
    ).not.toBeInTheDocument();
  });

  it("shows the install source in words, not the raw install_type value", () => {
    renderSettings(operatorMe, { updateStatus: windowsUpdateAvailableStatus });
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(screen.getByText("Windows installer")).toBeInTheDocument();
    expect(screen.queryByText("windows")).not.toBeInTheDocument();
  });

  it("does not show a success-looking pill for a failed update check", () => {
    renderSettings(operatorMe, {
      updateStatus: {
        ...minimalUpdateStatus,
        status: "unavailable",
        summary: "Could not reach the update server.",
      },
    });
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    const pill = within(
      screen.getByTestId("suite-settings-release-status"),
    ).getByText("Unavailable");
    expect(pill.className).not.toContain("healthy");
    expect(pill.className).toContain("failed");
  });

  function seededUpdateClient(overrides: {
    updateStatus: UpdateStatus;
    updateSettings?: {
      mode: "Auto" | "DownloadOnly" | "NotifyOnly";
      check_on_startup: boolean;
      check_interval_minutes: number;
    };
  }) {
    const qc = new QueryClient({
      defaultOptions: { queries: { retry: false, staleTime: Infinity } },
    });
    qc.setQueryData(settingsKeys.app, minimalAppSettings);
    qc.setQueryData(settingsKeys.securityOverview, minimalSecurity);
    qc.setQueryData(authKeys.me, operatorMe);
    qc.setQueryData(authKeys.session, minimalCurrentSession);
    qc.setQueryData(settingsKeys.configurationBackups, {
      directory: "C:/Weir/backups/suite-configuration",
      items: [],
    });
    qc.setQueryData(settingsKeys.updateStatus, overrides.updateStatus);
    if (overrides.updateSettings) {
      qc.setQueryData(settingsKeys.updateSettings, overrides.updateSettings);
      qc.setQueryData(settingsKeys.updateState, {
        downloaded: false,
        pending_version: null,
      });
    }
    qc.setQueryData(
      [
        ...settingsKeys.logs,
        {
          level: undefined,
          search: undefined,
          has_exception: undefined,
          limit: 250,
        },
      ],
      minimalLogs,
    );
    qc.setQueryData(settingsKeys.metrics, minimalMetrics);
    return qc;
  }

  it("gives Notify-only a clear Update button once an update is available", () => {
    const qc = seededUpdateClient({
      updateStatus: windowsUpdateAvailableStatus,
      updateSettings: {
        mode: "NotifyOnly",
        check_on_startup: true,
        check_interval_minutes: 60,
      },
    });

    render(wrap(<SystemPage />, qc));
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(
      screen.getByRole("link", { name: "Download the update" }),
    ).toHaveAttribute(
      "href",
      windowsUpdateAvailableStatus.windows_installer_url,
    );
  });

  it("hides Docker update instructions when there is no update to apply", () => {
    const dockerStatus: UpdateStatus = {
      ...minimalUpdateStatus,
      install_type: "docker",
      docker_update_command: "docker compose pull && docker compose up -d",
    };
    render(
      wrap(<SystemPage />, seededUpdateClient({ updateStatus: dockerStatus })),
    );
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(screen.queryByText("What happens next")).not.toBeInTheDocument();
  });

  it("shows Docker update instructions with a Copy button once an update exists", () => {
    const dockerStatus: UpdateStatus = {
      ...minimalUpdateStatus,
      install_type: "docker",
      status: "update_available",
      docker_update_command: "docker compose pull && docker compose up -d",
    };
    render(
      wrap(<SystemPage />, seededUpdateClient({ updateStatus: dockerStatus })),
    );
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(screen.getByText("What happens next")).toBeInTheDocument();
    expect(
      screen.getByText("docker compose pull && docker compose up -d"),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Copy the update command" }),
    ).toBeInTheDocument();
  });

  it("does not render mojibake in the upgrade panel", () => {
    renderSettings(operatorMe, { updateStatus: windowsUpdateAvailableStatus });
    fireEvent.click(screen.getByRole("tab", { name: "About" }));

    expect(document.body.textContent).not.toContain("â");
    expect(document.body.textContent).not.toContain("Ã");
    expect(document.body.textContent).not.toContain("�");
  });

  it("says a protection row needs attention instead of claiming success", () => {
    renderSettings(operatorMe);
    fireEvent.click(screen.getByRole("tab", { name: "Security" }));

    // The default fixture has extra HTTPS hardening off, which is a row that needs a look.
    expect(
      screen.getByText(/protections .* rows? needs? attention/),
    ).toBeInTheDocument();
  });

  it("shows change password in Security", () => {
    renderSettings(operatorMe);
    fireEvent.click(screen.getByRole("tab", { name: "Security" }));
    expect(
      screen.getByRole("heading", { name: "Change password" }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("heading", { name: "How sign-in is protected" }),
    ).toBeInTheDocument();
  });

  it("change password fields use individually-named Show/Hide buttons and reset visibility when cleared", () => {
    renderSettings(operatorMe);
    fireEvent.click(screen.getByRole("tab", { name: "Security" }));
    const current = screen.getByPlaceholderText("Enter current password");
    expect(current).toHaveAttribute("type", "password");
    fireEvent.change(current, { target: { value: "current-secret" } });
    const showPassword = screen.getByRole("button", { name: "Show password" });
    expect(
      screen.getByRole("button", { name: "Show new password" }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "Show password confirmation" }),
    ).toBeInTheDocument();
    fireEvent.click(showPassword);
    expect(current).toHaveAttribute("type", "text");
    fireEvent.change(current, { target: { value: "" } });
    expect(current).toHaveAttribute("type", "password");
  });

  it("shows an on-screen confirmation after a successful password change", async () => {
    vi.spyOn(authApi, "postChangePassword").mockResolvedValue({
      message: "Password changed. Sign in again with your new password.",
    });
    renderSettings(operatorMe);
    fireEvent.click(screen.getByRole("tab", { name: "Security" }));

    fireEvent.change(screen.getByPlaceholderText("Enter current password"), {
      target: { value: "current-secret" },
    });
    fireEvent.change(screen.getByPlaceholderText("Enter new password"), {
      target: { value: "new-secret-pass" },
    });
    fireEvent.change(screen.getByPlaceholderText("Re-enter new password"), {
      target: { value: "new-secret-pass" },
    });
    fireEvent.click(screen.getByRole("button", { name: "Change password" }));

    await waitFor(() => {
      expect(
        screen.getByText(
          "Password changed. Sign in again with your new password.",
        ),
      ).toBeInTheDocument();
    });
    expect(screen.getByRole("link", { name: "Sign in again" })).toHaveAttribute(
      "href",
      "/login",
    );
  });
});
