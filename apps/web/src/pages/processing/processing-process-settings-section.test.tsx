import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as authQueries from "../../lib/auth/queries";
import * as filesAtOnceApi from "../../lib/processing/files-at-once-api";
import * as api from "../../lib/processing/operator-settings-api";
import type {
  ProcessingFilesAtOnceOut,
  ProcessingOperatorSettingsOut,
} from "../../lib/processing/types";
import { ProcessingProcessSettingsSection } from "./processing-process-settings-section";

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
  keep_failed_work_files: false,
  file_log_retention_days: 90,
  verbose_detection_logging: false,
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
  updated_at: "2026-09-01T04:00:00Z",
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

it("exposes the complete throughput, record, diagnostic, and cleanup contract", async () => {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockResolvedValue(settings);
  vi.spyOn(filesAtOnceApi, "fetchProcessingFilesAtOnce").mockResolvedValue(
    readout,
  );
  const save = vi
    .spyOn(api, "putProcessingOperatorSettings")
    .mockResolvedValue({
      ...settings,
      file_log_retention_days: 0,
      verbose_detection_logging: true,
    });

  render(<ProcessingProcessSettingsSection />, { wrapper });

  expect(
    await screen.findByRole("heading", { name: "Files at once" }),
  ).toBeInTheDocument();
  expect(screen.getByLabelText(/Budget \(units\)/)).toHaveValue(6);
  expect(screen.getByLabelText("1080p cost")).toHaveValue(2);
  expect(
    screen.getByRole("checkbox", { name: /Reclaim stale temporary files/ }),
  ).toBeChecked();
  expect(
    screen.getByRole("checkbox", {
      name: /Delete source after a terminal failure/,
    }),
  ).not.toBeChecked();

  fireEvent.change(
    screen.getByRole("spinbutton", {
      name: /Processing-record retention \(days\)/,
    }),
    { target: { value: "0" } },
  );
  fireEvent.click(
    screen.getByRole("checkbox", { name: /Verbose file-detection records/ }),
  );
  fireEvent.click(
    screen.getByRole("button", { name: "Save processing settings" }),
  );

  await waitFor(() => {
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({
        runner_capacity: 6,
        runner_cost_1080p: 2,
        runner_budget_enabled: true,
        work_temp_stale_sweep_enabled: true,
        failure_cleanup_enabled: false,
        keep_failed_work_files: false,
        file_log_retention_days: 0,
        verbose_detection_logging: true,
      }),
    );
  });
});

it("says what waiting files are waiting for, and hides the resolution budget until it is switched on (#633)", async () => {
  vi.spyOn(authQueries, "useMeQuery").mockReturnValue({
    data: { role: "operator" },
  } as ReturnType<typeof authQueries.useMeQuery>);
  vi.spyOn(api, "fetchProcessingOperatorSettings").mockResolvedValue({
    ...settings,
    runner_budget_enabled: false,
  });
  vi.spyOn(filesAtOnceApi, "fetchProcessingFilesAtOnce").mockResolvedValue(
    readout,
  );
  const save = vi
    .spyOn(api, "putProcessingOperatorSettings")
    .mockResolvedValue({ ...settings, runner_budget_enabled: true });

  render(<ProcessingProcessSettingsSection />, { wrapper });

  expect(
    await screen.findByTestId("processing-files-at-once-readout"),
  ).toHaveTextContent(
    "2 running now. 3 files are waiting for a free slot: 2 of 2 in use.",
  );
  expect(screen.queryByLabelText(/Budget \(units\)/)).not.toBeInTheDocument();

  fireEvent.click(
    screen.getByRole("checkbox", { name: /Also weigh files by resolution/ }),
  );
  expect(screen.getByLabelText(/Budget \(units\)/)).toHaveValue(6);
  fireEvent.click(
    screen.getByRole("button", { name: "Save processing settings" }),
  );

  await waitFor(() => {
    expect(save).toHaveBeenCalledWith(
      expect.objectContaining({ runner_budget_enabled: true }),
    );
  });
});
