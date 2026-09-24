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

// Each tab's own content is tested beside it; this file is about which tab is showing.
vi.mock("../../components/shell/pause-control", () => ({
  PauseControl: () => null,
}));
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
vi.mock("./tabs/performance/direct-play-section", () => ({
  DirectPlaySection: () => null,
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

function selectedTab(): string | null {
  return screen.getByRole("tab", { selected: true }).textContent;
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

  it("opens on the tab the address names, including the names 3.1 used", () => {
    renderAt("/settings?tab=schedules");
    expect(selectedTab()).toBe("Schedule");
    expect(screen.getByText("Schedule content")).toBeInTheDocument();
  });

  it("puts the tab in the address, so it can be bookmarked and gone back to", () => {
    const router = renderAt("/settings");
    expect(selectedTab()).toBe("Libraries");

    fireEvent.click(screen.getByRole("tab", { name: "Alerts" }));
    expect(router.state.location.search).toBe("?tab=alerts");
    expect(screen.getByText("Alerts content")).toBeInTheDocument();
  });

  it("follows the address when it changes without the page remounting: Back, Forward and the side menu", async () => {
    const router = renderAt("/settings");
    fireEvent.click(screen.getByRole("tab", { name: "Rules" }));
    expect(selectedTab()).toBe("Rules");

    await act(() => router.navigate(-1));
    expect(router.state.location.search).toBe("");
    expect(selectedTab()).toBe("Libraries");
    expect(screen.getByText("Libraries content")).toBeInTheDocument();

    await act(() => router.navigate(1));
    expect(selectedTab()).toBe("Rules");

    // The side menu's Settings link is a plain /settings.
    await act(() => router.navigate("/settings"));
    expect(selectedTab()).toBe("Libraries");
  });

  describe("with unsaved changes on a tab", () => {
    afterEach(() => {
      alerts.unsaved = null;
    });

    it("asks before moving to another tab, and stays when told to", () => {
      alerts.unsaved = "the Discord alert";
      const router = renderAt("/settings?tab=alerts");

      fireEvent.click(screen.getByRole("tab", { name: "Rules" }));

      expect(screen.getByTestId("settings-unsaved-changes")).toHaveTextContent(
        "You have unsaved changes to the Discord alert. Leave without saving?",
      );
      fireEvent.click(screen.getByTestId("settings-unsaved-changes-cancel"));
      expect(router.state.location.search).toBe("?tab=alerts");
      expect(selectedTab()).toBe("Alerts");
    });

    it("moves on once the person chooses to leave without saving", async () => {
      alerts.unsaved = "the Discord alert";
      const router = renderAt("/settings?tab=alerts");

      fireEvent.click(screen.getByRole("tab", { name: "Rules" }));
      await act(async () => {
        fireEvent.click(screen.getByTestId("settings-unsaved-changes-confirm"));
      });

      expect(router.state.location.search).toBe("?tab=rules");
      expect(selectedTab()).toBe("Rules");
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
