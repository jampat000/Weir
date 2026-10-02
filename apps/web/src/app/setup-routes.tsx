import type { ComponentType, ReactNode } from "react";
import { Navigate, type RouteObject } from "react-router-dom";

import {
  SETUP_AREAS,
  type SetupArea,
  type SetupTabId,
} from "../lib/settings/setup-areas";

type TabComponent = { Component: ComponentType };

/** Each tab's page, fetched when the tab is first opened. */
const SETUP_TAB_PAGES: Record<SetupTabId, () => Promise<TabComponent>> = {
  workflows: async () => ({
    Component: (await import("../pages/settings/tabs/libraries/libraries-tab"))
      .LibrariesTab,
  }),
  schedule: async () => ({
    Component: (await import("../pages/settings/tabs/schedule/schedule-tab"))
      .ScheduleTab,
  }),
  managers: async () => ({
    Component: (
      await import("../pages/settings/tabs/media-managers/media-managers-tab")
    ).MediaManagersTab,
  }),
  "download-clients": async () => ({
    Component: (
      await import("../pages/settings/tabs/media-managers/download-clients-tab")
    ).DownloadClientsTab,
  }),
  alerts: async () => ({
    Component: (await import("../pages/settings/tabs/alerts/alerts-tab"))
      .AlertsTab,
  }),
  profiles: async () => ({
    Component: (await import("../pages/settings/tabs/rules/profiles-tab"))
      .ProfilesTab,
  }),
  metadata: async () => ({
    Component: (await import("../pages/settings/tabs/rules/metadata-tab"))
      .MetadataTab,
  }),
  devices: async () => ({
    Component: (await import("../pages/settings/tabs/rules/devices-tab"))
      .DevicesTab,
  }),
  speed: async () => ({
    Component: (await import("../pages/settings/tabs/performance/speed-tab"))
      .SpeedTab,
  }),
  cleanup: async () => ({
    Component: (await import("../pages/settings/tabs/cleanup/cleanup-tab"))
      .CleanupTab,
  }),
  timers: async () => ({
    Component: (await import("../pages/settings/tabs/performance/timers-tab"))
      .TimersTab,
  }),
};

/**
 * One layout route per area and one child per tab: the first tab at the area's own address, and each other tab
 * under its id. The first tab's id also leads there, so `/setup/connections/managers` is not a dead end.
 * There is no route for `/setup` itself, which is where the first sign-in is created.
 */
function routesForArea(area: SetupArea, errorElement: ReactNode): RouteObject {
  const [first, ...others] = area.tabs;
  return {
    path: `setup/${area.id}`,
    lazy: async () => ({
      Component: (await import("../pages/settings/setup-area-layout"))
        .SetupAreaLayout,
    }),
    errorElement,
    children: [
      {
        index: true,
        lazy: SETUP_TAB_PAGES[first.id],
        errorElement,
      },
      {
        path: first.id,
        element: <Navigate to={area.path} replace />,
      },
      ...others.map((tab): RouteObject => ({
        path: tab.id,
        lazy: SETUP_TAB_PAGES[tab.id],
        errorElement,
      })),
    ],
  };
}

/** The four setup areas, as children of the signed-in shell. */
export function setupRoutes(errorElement: ReactNode): RouteObject[] {
  return SETUP_AREAS.map((area) => routesForArea(area, errorElement));
}
