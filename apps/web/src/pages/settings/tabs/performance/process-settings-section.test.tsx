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

import * as authQueries from "../../../../lib/auth/queries";
import * as filesAtOnceApi from "../../../../lib/processing/files-at-once-api";
import * as api from "../../../../lib/processing/operator-settings-api";
import type {
  ProcessingFilesAtOnceOut,
  ProcessingOperatorSettingsOut,
} from "../../../../lib/processing/types";
import { ProcessSettingsSection } from "./process-settings-section";

const settings: ProcessingOperatorSettingsOut = {
  max_concurrent_files: 2,
  runner_capacity: 6,
  runner_cost_sd: 1,
  runner_cost_720p: 1,
  runner_cost_1080p: 2,
  runner_cost_4k: 4,
  runner_cost_undetermined: 0,
  runner_budget_enabled: true,
  work_temp_stale_sweep_enabled: true,
  failure_cleanup_enabled: false,
  unclaimed_handback_cleanup_enabled: false,
  unclaimed_handback_window_days: 14,
  keep_failed_work_files: false,
  file_log_retention_days: 90,
  min_file_age_seconds: 60,
  min_input_file_size_mb: 50,
  minimum_free_disk_space_mb: 5120,
  movie_schedule_enabled: true,
  movie_schedule_hours_limited: false,
  movie_schedule_days: "",
  movie_schedule_start: "00:00",
  movie_schedule_end: "23:59",
  tv_schedule_enabled: true,
  tv_schedule_hours_limited: false,
  tv_schedule_days: "",
  tv_schedule_start: "00:00",
  tv_schedule_end: "23:59",
  schedule_timezone: "Australia/Sydney",
  updated_at: "2026-07-28T04:00:00Z",
};

const readout: ProcessingFilesAtOnceOut = {
  files_at_once: 2,
  worker_slots: 10,
  effective_files_at_once: 2,
  running: 2,
  waiting: 3,
  waiting_for: "free_slot",
  message: "3 files are waiting for a free slot: 2 of 2 in use.",
  slots_note: "",
};

function wrapper({ children }: { children: ReactNode }) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>;
}

afterEach(() => {
  vi.restoreAllMocks();
});

function mockSignedIn() {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(filesAtOnceApi, "fetchProcessingFilesAtOnce").mockResolvedValue(
    readout,
  );
}

it("saves Files at once up to ten, and the budget and checks with it", async () => {
  mockSignedIn();
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockResolvedValue(settings);
  const save = vi
    .spyOn(api, "putProcessingOperatorSettings")
    .mockResolvedValue({ ...settings, max_concurrent_files: 10 });

  render(<ProcessSettingsSection />, { wrapper });

  const choices = await screen.findByRole("group", { name: "Files at once" });
  expect(
    screen.getByText("Nothing changes until you press Save."),
  ).toBeInTheDocument();
  expect(screen.getByRole("button", { name: "2" })).toHaveAttribute(
    "aria-pressed",
    "true",
  );
  expect(choices.querySelectorAll("button")).toHaveLength(10);
  expect(screen.getByLabelText("Budget")).toHaveValue(6);
  expect(screen.getByLabelText("A 1080p file costs")).toHaveValue(2);
  expect(screen.getByLabelText("Wait until it stops changing for")).toHaveValue(
    60,
  );

  // #633: files at once goes up to ten.
  fireEvent.click(screen.getByRole("button", { name: "10" }));
  fireEvent.click(
    screen.getByRole("button", { name: "Save performance settings" }),
  );

  await waitFor(() => {
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({
        max_concurrent_files: 10,
        runner_capacity: 6,
        runner_cost_1080p: 2,
        runner_budget_enabled: true,
        keep_failed_work_files: false,
        min_file_age_seconds: 60,
      }),
    );
  });
  // The cleanup switches and record keeping are Cleanup's; Performance never sends them.
  const body = save.mock.calls[0][0];
  expect(body).not.toHaveProperty("work_temp_stale_sweep_enabled");
  expect(body).not.toHaveProperty("failure_cleanup_enabled");
  expect(body).not.toHaveProperty("file_log_retention_days");
  expect(body).not.toHaveProperty("verbose_detection_logging");
  expect(
    screen.queryByText(/Verbose file-detection records/),
  ).not.toBeInTheDocument();
});

it("formats an ugly free-space decimal sensibly, and is not dirty on load", async () => {
  mockSignedIn();
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockResolvedValue({
    ...settings,
    minimum_free_disk_space_mb: 5000,
  });

  render(<ProcessSettingsSection />, { wrapper });

  const field = await screen.findByLabelText("Keep free on the output drive");
  expect(field).toHaveValue(4.88);
  expect(
    screen.getByRole("button", { name: "No changes to save" }),
  ).toBeDisabled();
});

it("shows a load error instead of a blank panel when performance settings fail to load", async () => {
  mockSignedIn();
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockRejectedValue(
    new Error("network down"),
  );

  render(<ProcessSettingsSection />, { wrapper });

  expect(await screen.findByTestId("settings-load-error")).toHaveTextContent(
    "Weir couldn’t load your performance settings. Reload the page to try again.",
  );
});

it("says what waiting files are waiting for, and hides the resolution budget until it is switched on (#633)", async () => {
  mockSignedIn();
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockResolvedValue({
    ...settings,
    runner_budget_enabled: false,
  });
  const save = vi
    .spyOn(api, "putProcessingOperatorSettings")
    .mockResolvedValue({ ...settings, runner_budget_enabled: true });

  render(<ProcessSettingsSection />, { wrapper });

  expect(
    await screen.findByTestId("processing-files-at-once-readout"),
  ).toHaveTextContent(
    "2 running now. 3 files are waiting for a free slot: 2 of 2 in use.",
  );
  expect(screen.queryByLabelText("Budget")).not.toBeInTheDocument();

  const budgetSwitch = screen.getByRole("radiogroup", {
    name: "Also weigh files by resolution",
  });
  fireEvent.click(within(budgetSwitch).getByRole("radio", { name: "On" }));
  expect(screen.getByLabelText("Budget")).toHaveValue(6);
  fireEvent.click(
    screen.getByRole("button", { name: "Save performance settings" }),
  );

  await waitFor(() => {
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({ runner_budget_enabled: true }),
    );
  });
});
