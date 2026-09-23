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
vi.mock("../processing/processing-libraries-section", () => ({
  ProcessingLibrariesSection: () => <div>Libraries content</div>,
}));
vi.mock("../processing/processing-remux-section", () => ({
  ProcessingRemuxSection: () => <div>Rules content</div>,
}));
vi.mock("./settings-media-managers-tab", () => ({
  SettingsMediaManagersTab: () => <div>Media managers content</div>,
}));
vi.mock("../processing/processing-process-settings-section", () => ({
  ProcessingProcessSettingsSection: () => <div>Running content</div>,
}));
vi.mock("../processing/processing-direct-play-section", () => ({
  ProcessingDirectPlaySection: () => null,
}));
vi.mock("../processing/processing-maintenance-section", () => ({
  ProcessingMaintenanceSection: () => <div>Housekeeping content</div>,
}));
vi.mock("../processing/processing-schedules-section", () => ({
  ProcessingSchedulesSection: () => <div>Schedule content</div>,
}));
vi.mock("./settings-notifications-tab", () => ({
  SettingsNotificationsTab: () => <div>Alerts content</div>,
}));

function renderAt(entry: string) {
  const router = createMemoryRouter(
    [{ path: "/settings", element: <SettingsPage /> }],
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
});
