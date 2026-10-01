import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
} from "@testing-library/react";
import { createMemoryRouter, RouterProvider } from "react-router-dom";
import { afterEach, describe, expect, it, vi } from "vitest";
import { SettingsPage } from "./settings-page";

// Each section's own content is tested beside it; this file is about which section is showing.
vi.mock("./tabs/libraries/libraries-tab", () => ({
  LibrariesTab: () => <div>Libraries content</div>,
}));
vi.mock("./tabs/rules/rules-tab", () => ({
  RulesTab: () => <div>Rules content</div>,
}));
vi.mock("./tabs/media-managers/media-managers-tab", () => ({
  MediaManagersTab: () => <div>Media managers content</div>,
}));
vi.mock("./tabs/performance/process-settings-section", () => ({
  ProcessSettingsSection: () => <div>Running content</div>,
}));
vi.mock("./tabs/cleanup/cleanup-tab", () => ({
  CleanupTab: () => <div>Housekeeping content</div>,
}));
vi.mock("./tabs/schedule/schedule-tab", () => ({
  ScheduleTab: () => <div>Schedule content</div>,
}));
// Alerts stands in for any panel with an edit not yet saved; the flag says whether it has one.
const alerts = vi.hoisted(() => ({ unsaved: null as string | null }));
vi.mock("./tabs/alerts/alerts-tab", async () => {
  const { useUnsavedChanges } = await import("./unsaved-changes");
  return {
    AlertsTab: () => {
      useUnsavedChanges(alerts.unsaved);
      return <div>Alerts content</div>;
    },
  };
});

function renderAt(entry: string) {
  const router = createMemoryRouter(
    [
      { path: "/settings", element: <SettingsPage /> },
      { path: "/system", element: <div>System page</div> },
    ],
    { initialEntries: [entry] },
  );
  render(<RouterProvider router={router} />);
  return router;
}

afterEach(() => cleanup());

describe("SettingsPage", () => {
  it.each([
    ["upgrade", "?tab=about"],
    ["support", "?tab=about"],
    ["backup", "?tab=backups"],
    ["security", "?tab=security"],
    ["logs", "?tab=logs"],
  ])("sends the 3.1 %s tab to its place on System", (oldTab, systemSearch) => {
    const router = renderAt(`/settings?tab=${oldTab}`);
    expect(router.state.location.pathname).toBe("/system");
    expect(router.state.location.search).toBe(systemSearch);
    expect(screen.getByText("System page")).toBeTruthy();
  });

  it("opens on the section the address names, including the names 3.1 used", () => {
    renderAt("/settings?tab=schedules");

    expect(screen.getByRole("region", { name: "Schedule" })).toHaveTextContent(
      "Schedule content",
    );
  });

  it("opens on Workflows when the address names no section", () => {
    renderAt("/settings");

    expect(screen.getByRole("region", { name: "Workflows" })).toHaveTextContent(
      "Libraries content",
    );
  });

  it("has no row of tabs of its own: the side menu is the way between sections", () => {
    renderAt("/settings");

    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
  });

  it("follows the address when it changes without the page remounting: Back, Forward and the side menu", async () => {
    const router = renderAt("/settings");
    await act(() => router.navigate("/settings?tab=rules"));
    expect(screen.getByText("Rules content")).toBeInTheDocument();

    await act(() => router.navigate(-1));
    expect(router.state.location.search).toBe("");
    expect(screen.getByText("Libraries content")).toBeInTheDocument();

    await act(() => router.navigate(1));
    expect(screen.getByText("Rules content")).toBeInTheDocument();

    await act(() => router.navigate("/settings?tab=alerts"));
    expect(screen.getByText("Alerts content")).toBeInTheDocument();
  });

  describe("with unsaved changes in a section", () => {
    afterEach(() => {
      alerts.unsaved = null;
    });

    it("asks before moving to another section, and stays when told to", async () => {
      alerts.unsaved = "the Discord alert";
      const router = renderAt("/settings?tab=alerts");

      await act(() => router.navigate("/settings?tab=rules"));

      expect(screen.getByTestId("settings-unsaved-changes")).toHaveTextContent(
        "You have unsaved changes to the Discord alert. Leave without saving?",
      );
      fireEvent.click(screen.getByTestId("settings-unsaved-changes-cancel"));
      expect(router.state.location.search).toBe("?tab=alerts");
      expect(screen.getByText("Alerts content")).toBeInTheDocument();
    });

    it("moves on once the person chooses to leave without saving", async () => {
      alerts.unsaved = "the Discord alert";
      const router = renderAt("/settings?tab=alerts");

      await act(() => router.navigate("/settings?tab=rules"));
      await act(async () => {
        fireEvent.click(screen.getByTestId("settings-unsaved-changes-confirm"));
      });

      expect(router.state.location.search).toBe("?tab=rules");
      expect(screen.getByText("Rules content")).toBeInTheDocument();
    });

    it("asks before leaving Settings for another page", async () => {
      alerts.unsaved = "the Discord alert";
      const router = renderAt("/settings?tab=alerts");

      await act(() => router.navigate("/system"));

      expect(router.state.location.pathname).toBe("/settings");
      expect(
        screen.getByTestId("settings-unsaved-changes"),
      ).toBeInTheDocument();
    });
  });
});
