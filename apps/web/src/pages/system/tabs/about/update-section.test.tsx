import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { UpdateStatus } from "../../../../lib/settings/types";
import { UpdateSection } from "./update-section";

const mocks = vi.hoisted(() => ({
  useUpdateStatusQuery: vi.fn(),
  useUpdateSettingsQuery: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStatusQuery: () => mocks.useUpdateStatusQuery(),
  useUpdateSettingsQuery: () => mocks.useUpdateSettingsQuery(),
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));

vi.mock("./update-ready-notice", () => ({ UpdateReadyNotice: () => null }));
vi.mock("./update-preferences", () => ({ UpdatePreferences: () => null }));

const LIMITED =
  "GitHub is limiting update checks from your network right now. Weir will check again at 11:35 pm.";

function status(
  limited: Pick<UpdateStatus, "summary"> &
    Pick<
      UpdateStatus,
      | "latest_version"
      | "published_at"
      | "retry_at"
      | "known_update_available"
      | "docker_update_command"
      | "windows_installer_url"
    >,
  installType = "source",
): UpdateStatus {
  return {
    current_version: "3.2.16",
    install_type: installType,
    in_app_upgrade_supported: false,
    status: "rate_limited",
    ...limited,
  };
}

function showStatus(update: UpdateStatus) {
  mocks.useUpdateStatusQuery.mockReturnValue({
    data: update,
    isPending: false,
    isFetching: false,
    refetch: vi.fn(),
  });
  render(<UpdateSection />);
}

describe("UpdateSection while GitHub limits update checks", () => {
  beforeEach(() => {
    mocks.useUpdateStatusQuery.mockReset();
    mocks.useUpdateSettingsQuery.mockReturnValue({ data: undefined });
  });

  it("says plainly when Weir will check again", () => {
    showStatus(
      status({
        summary: LIMITED,
        retry_at: "2026-01-15T23:35:00Z",
      }),
    );

    const line = screen.getByTestId("suite-settings-release-status");
    expect(line).toHaveTextContent("Limited by GitHub");
    expect(line).toHaveTextContent(LIMITED);
    expect(
      screen.queryByTestId("suite-settings-last-known-release"),
    ).toBeNull();
  });

  it("keeps the last release it knew of in view", () => {
    showStatus(
      status({
        summary: LIMITED,
        latest_version: "3.3.0",
        published_at: "2026-10-01T10:00:00Z",
      }),
    );

    expect(
      screen.getByTestId("suite-settings-last-known-release"),
    ).toHaveTextContent(/Latest known: 3\.3\.0, published .*2026/);
  });

  it("still offers the update Weir already knew of: the Docker command", () => {
    showStatus(
      status(
        {
          summary: LIMITED,
          latest_version: "3.3.0",
          known_update_available: true,
          docker_update_command: "docker compose pull && docker compose up -d",
        },
        "docker",
      ),
    );

    expect(
      screen.getByText("docker compose pull && docker compose up -d"),
    ).toBeInTheDocument();
  });

  it("still offers the update Weir already knew of: the download link in notify-only mode", () => {
    mocks.useUpdateSettingsQuery.mockReturnValue({
      data: { mode: "NotifyOnly" },
    });
    showStatus(
      status(
        {
          summary: LIMITED,
          latest_version: "3.3.0",
          known_update_available: true,
          windows_installer_url: "https://example.test/Weir-win-Setup.exe",
        },
        "windows",
      ),
    );

    expect(
      screen.getByRole("link", { name: "Download the update" }),
    ).toHaveAttribute("href", "https://example.test/Weir-win-Setup.exe");
  });

  it("offers no update when the last release it knew of is not newer", () => {
    showStatus(
      status(
        {
          summary: LIMITED,
          latest_version: "3.2.16",
          known_update_available: false,
          docker_update_command: "docker compose pull && docker compose up -d",
        },
        "docker",
      ),
    );

    expect(screen.queryByText(/docker compose pull/)).toBeNull();
  });

  it("still offers Check again, which the server answers without asking GitHub", () => {
    showStatus(status({ summary: LIMITED }));

    expect(screen.getByRole("button", { name: "Check again →" })).toBeEnabled();
  });
});
