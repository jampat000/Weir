import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../lib/auth/queries";
import type { MaintenanceState } from "../../lib/processing/maintenance-api";
import * as maintenanceQueries from "../../lib/processing/maintenance-queries";
import * as processingQueries from "../../lib/processing/queries";
import { ProcessingMaintenanceSection } from "./processing-maintenance-section";

const mutate = vi.fn();
const saveSettings = vi.fn();

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

function state(over: Partial<MaintenanceState> = {}): MaintenanceState {
  return {
    families: [
      {
        family: "work_temp_stale_sweep",
        enabled: true,
        description: "Reclaims Weir's own stale working files.",
        pending: 0,
        running: 0,
        last_completed_at: null,
        last_failed_at: null,
        last_error: null,
        interval_seconds: 3600,
        next_run_at: "2026-09-23T05:00:00",
      },
      {
        family: "failure_cleanup",
        enabled: false,
        description:
          "Removes the source release folder after a file has failed terminally. This deletes the original.",
        pending: 0,
        running: 0,
        last_completed_at: null,
        last_failed_at: null,
        last_error: null,
        interval_seconds: 3600,
        next_run_at: null,
      },
    ],
    ...over,
  };
}

function setup(data: MaintenanceState, role = "operator") {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(maintenanceQueries, "useProcessingMaintenanceQuery").mockReturnValue(
    {
      data,
    } as ReturnType<typeof maintenanceQueries.useProcessingMaintenanceQuery>,
  );
  vi.spyOn(
    maintenanceQueries,
    "useProcessingRuntimeSettingsQuery",
  ).mockReturnValue({ data: undefined } as ReturnType<
    typeof maintenanceQueries.useProcessingRuntimeSettingsQuery
  >);
  vi.spyOn(
    processingQueries,
    "useProcessingOperatorSettingsQuery",
  ).mockReturnValue({
    data: { file_log_retention_days: 90 },
  } as ReturnType<typeof processingQueries.useProcessingOperatorSettingsQuery>);
  vi.spyOn(
    processingQueries,
    "useProcessingOperatorSettingsSaveMutation",
  ).mockReturnValue({
    mutateAsync: saveSettings,
    isPending: false,
  } as unknown as ReturnType<
    typeof processingQueries.useProcessingOperatorSettingsSaveMutation
  >);
  vi.spyOn(maintenanceQueries, "useRunProcessingMaintenance").mockReturnValue({
    mutateAsync: mutate,
    isPending: false,
  } as unknown as ReturnType<
    typeof maintenanceQueries.useRunProcessingMaintenance
  >);
}

afterEach(() => {
  vi.restoreAllMocks();
  mutate.mockReset();
  saveSettings.mockReset();
});

it("lists each job with its switch, how often it runs and when it next does", async () => {
  setup(state());

  render(<ProcessingMaintenanceSection />, { wrapper });

  const sweep = await screen.findByTestId(
    "processing-maintenance-work_temp_stale_sweep",
  );
  expect(sweep).toHaveTextContent("Leftover work files");
  expect(within(sweep).getByRole("radio", { name: "On" })).toHaveAttribute(
    "aria-checked",
    "true",
  );
  expect(
    within(sweep).getByRole("combobox", {
      name: "How often Leftover work files runs",
    }),
  ).toHaveValue("3600");
  const cleanup = screen.getByTestId("processing-maintenance-failure_cleanup");
  expect(cleanup).toHaveTextContent("Downloads of failed files");
  expect(within(cleanup).getByRole("radio", { name: "Off" })).toHaveAttribute(
    "aria-checked",
    "true",
  );
  expect(cleanup).toHaveTextContent("Off");
});

it("switches a job on and changes how often it runs, straight away", async () => {
  setup(state());
  saveSettings.mockResolvedValue({});

  render(<ProcessingMaintenanceSection />, { wrapper });
  const cleanup = await screen.findByTestId(
    "processing-maintenance-failure_cleanup",
  );
  fireEvent.click(within(cleanup).getByRole("radio", { name: "On" }));
  await waitFor(() =>
    expect(saveSettings).toHaveBeenCalledWith({
      failure_cleanup_enabled: true,
    }),
  );
  expect(
    await screen.findByTestId("processing-maintenance-notice"),
  ).toHaveTextContent("Downloads of failed files is on.");

  fireEvent.change(
    within(cleanup).getByRole("combobox", {
      name: "How often Downloads of failed files runs",
    }),
    { target: { value: "86400" } },
  );
  await waitFor(() =>
    expect(saveSettings).toHaveBeenCalledWith({
      failure_cleanup_interval_seconds: 86400,
    }),
  );
});

it("carries the warning where the switch is", async () => {
  setup(state());

  render(<ProcessingMaintenanceSection />, { wrapper });

  // Failure cleanup deletes originals, and the operator sees that beside the button.
  expect(
    await screen.findByTestId("processing-maintenance-failure_cleanup"),
  ).toHaveTextContent(/deletes the original/i);
});

it("runs a job for both kinds of library", async () => {
  setup(state());
  mutate.mockResolvedValue({
    queued: true,
    detail: "Queued a work file sweep.",
  });

  render(<ProcessingMaintenanceSection />, { wrapper });
  fireEvent.click(
    await screen.findByTestId(
      "processing-maintenance-run-work_temp_stale_sweep",
    ),
  );

  await waitFor(() =>
    expect(mutate).toHaveBeenCalledWith({
      family: "work_temp_stale_sweep",
      mediaScope: "tv",
    }),
  );
  expect(mutate).toHaveBeenCalledWith({
    family: "work_temp_stale_sweep",
    mediaScope: "movie",
  });
});

it("shows the server's own words when nothing was queued", async () => {
  setup(state());
  mutate.mockResolvedValue({
    queued: false,
    detail: "A failure cleanup for this scope is already waiting or running.",
  });

  render(<ProcessingMaintenanceSection />, { wrapper });
  fireEvent.click(
    await screen.findByTestId("processing-maintenance-run-failure_cleanup"),
  );

  expect(
    await screen.findByTestId("processing-maintenance-notice"),
  ).toHaveTextContent(/already waiting or running/);
});

it("reports a running family rather than showing it as idle", async () => {
  setup(
    state({
      families: [
        {
          family: "work_temp_stale_sweep",
          enabled: true,
          description: "Reclaims stale working files.",
          pending: 0,
          running: 1,
          last_completed_at: null,
          last_failed_at: null,
          last_error: null,
        },
      ],
    }),
  );

  render(<ProcessingMaintenanceSection />, { wrapper });

  expect(
    await screen.findByTestId("processing-maintenance-work_temp_stale_sweep"),
  ).toHaveTextContent(/Running now/);
});

it("surfaces the reason a run failed", async () => {
  setup(
    state({
      families: [
        {
          family: "failure_cleanup",
          enabled: true,
          description: "Removes the source release folder.",
          pending: 0,
          running: 0,
          last_completed_at: null,
          last_failed_at: "2026-08-26T14:00:00Z",
          last_error: "The work folder was missing.",
        },
      ],
    }),
  );

  render(<ProcessingMaintenanceSection />, { wrapper });

  expect(
    await screen.findByTestId("processing-maintenance-failure_cleanup"),
  ).toHaveTextContent("The work folder was missing.");
});

it("does not offer a viewer the run buttons", async () => {
  setup(state(), "viewer");

  render(<ProcessingMaintenanceSection />, { wrapper });

  await screen.findByTestId("processing-maintenance-work_temp_stale_sweep");
  expect(
    screen.queryByTestId("processing-maintenance-run-work_temp_stale_sweep"),
  ).not.toBeInTheDocument();
});
