import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { UpdateStatus } from "../../../../lib/settings/types";
import { UpdateSection } from "./update-section";

const mocks = vi.hoisted(() => ({
  useUpdateStatusQuery: vi.fn(),
}));

vi.mock("../../../../lib/settings/queries", () => ({
  useUpdateStatusQuery: () => mocks.useUpdateStatusQuery(),
  useAppSettingsQuery: () => ({ data: { app_timezone: "UTC" } }),
}));

vi.mock("./update-actions", () => ({
  UpdateActions: () => <div data-testid="update-actions" />,
}));
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
      | "release_url"
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

  it("keeps the installer as a small link on Windows, where the update buttons take the update", () => {
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
      screen.getByRole("link", { name: "Download installer →" }),
    ).toHaveAttribute("href", "https://example.test/Weir-win-Setup.exe");
    expect(screen.getByTestId("update-actions")).toBeInTheDocument();
    expect(
      screen.queryByRole("link", { name: "Download the update" }),
    ).toBeNull();
    expect(screen.queryByRole("button", { name: "Check again →" })).toBeNull();
  });

  it("offers the update buttons on Windows only", () => {
    showStatus(status({ summary: LIMITED }, "docker"));

    expect(screen.queryByTestId("update-actions")).toBeNull();
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

  describe("when an update is available on Windows", () => {
    function showAvailable() {
      showStatus({
        ...status(
          {
            summary: "Weir 9.9.9 is available.",
            latest_version: "9.9.9",
            windows_installer_url: "https://example.test/Weir-win-Setup.exe",
            release_url: "https://example.test/release",
          },
          "windows",
        ),
        status: "update_available",
      });
    }

    it("says it is available, not ready, and says which version in a sentence", () => {
      showAvailable();

      const line = screen.getByTestId("suite-settings-release-status");
      expect(line).toHaveTextContent("Update available");
      expect(line).not.toHaveTextContent("Update ready");
      expect(line).toHaveTextContent(
        "Weir v9.9.9 is available (you have v3.2.16)",
      );
    });

    it("keeps the installer and the release notes as quiet links under the buttons, not controls in the header", () => {
      showAvailable();

      for (const name of ["Download installer →", "Release notes →"]) {
        const link = screen.getByRole("link", { name });
        expect(link).toHaveClass("mm-quiet-link");
        expect(link.closest(".mm-panel__aside")).toBeNull();
      }
    });
  });

  it("says which version is running when Weir is up to date", () => {
    showStatus({ ...status({ summary: "" }, "windows"), status: "up_to_date" });

    expect(
      screen.getByTestId("suite-settings-release-status"),
    ).toHaveTextContent("You have Weir v3.2.16");
  });
});
