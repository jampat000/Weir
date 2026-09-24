import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";

import * as api from "../../../../lib/processing/libraries-api";
import * as managersApi from "../../../../lib/processing/library-managers-api";
import * as modeApi from "../../../../lib/processing/library-mode-api";
import { LibrariesTab } from "./libraries-tab";
import { asOperator, library, wrapper } from "./library-test-fixtures";

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows Saving… while a library saves, and a failure inside the editor", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);
  let fail: (error: Error) => void = () => undefined;
  vi.spyOn(api, "createProcessingLibrary").mockReturnValue(
    new Promise((_, reject) => {
      fail = reject;
    }),
  );

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByTestId("processing-library-add"));
  fireEvent.change(screen.getByPlaceholderText("Movies 4K"), {
    target: { value: "Kids" },
  });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(screen.getByTestId("processing-library-save")).toHaveTextContent(
      "Saving…",
    ),
  );
  fail(new Error("The watched folder does not exist."));
  const form = screen.getByTestId("processing-library-form");
  expect(
    await within(form).findByTestId("processing-library-save-error"),
  ).toHaveTextContent("The watched folder does not exist.");
});

it("asks before closing the editor with unsaved edits, and keeps them when told to stay", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  fireEvent.change(screen.getByPlaceholderText("Movies 4K"), {
    target: { value: "Films" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

  expect(screen.getByTestId("settings-unsaved-changes")).toHaveTextContent(
    "You have unsaved changes to Movies. Leave without saving?",
  );
  fireEvent.click(screen.getByTestId("settings-unsaved-changes-cancel"));
  expect(screen.getByPlaceholderText("Movies 4K")).toHaveValue("Films");
});

it("closes an unchanged editor without asking", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));

  expect(
    screen.queryByTestId("processing-library-form"),
  ).not.toBeInTheDocument();
});

it("undoes an added existing-files folder when the editor is left without saving", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);
  vi.spyOn(modeApi, "fetchLibrarySettings").mockResolvedValue({
    library_folders: [],
    library_schedule_enabled: false,
    clean_hardlinked_files: false,
    skip_if_manager_would_redownload: true,
    keep_original_after_clean: false,
    originals_folder: "",
  });
  const saveFolders = vi.spyOn(modeApi, "saveLibrarySettings");

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  fireEvent.change(await screen.findByLabelText("Folder to add"), {
    target: { value: "/media/movies" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Add folder" }));
  fireEvent.click(screen.getByRole("button", { name: "Cancel" }));
  fireEvent.click(screen.getByTestId("settings-unsaved-changes-confirm"));

  expect(
    screen.queryByTestId("processing-library-form"),
  ).not.toBeInTheDocument();
  expect(saveFolders).not.toHaveBeenCalled();
});

it("labels hardware decoding as not available, since Weir never decodes the video", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));

  expect(screen.getByTestId("library-hardware-unavailable")).toHaveTextContent(
    "Not available in this version",
  );
  expect(
    screen.getByRole("combobox", { name: "Hardware decoding" }),
  ).toBeDisabled();
});

it("adds a library through the API", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);
  const create = vi
    .spyOn(api, "createProcessingLibrary")
    .mockResolvedValue(library({ id: 9, name: "Kids" }));

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByTestId("processing-library-add"));
  fireEvent.change(screen.getByPlaceholderText("Movies 4K"), {
    target: { value: "Kids" },
  });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() => {
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Kids", media_type: "movie" }),
    );
  });
});

it("starts a new library on the same files-at-once as Performance, and lets it be held lower (#633)", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([library()]);
  const create = vi
    .spyOn(api, "createProcessingLibrary")
    .mockResolvedValue(library({ id: 9, name: "Kids" }));

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByTestId("processing-library-add"));
  fireEvent.change(screen.getByPlaceholderText("Movies 4K"), {
    target: { value: "Kids" },
  });
  const filesAtOnce = screen.getByRole("combobox", { name: /Files at once/ });
  expect(filesAtOnce).toHaveValue("0");
  expect(filesAtOnce).toHaveTextContent("Same as Performance");
  expect(filesAtOnce).toHaveTextContent("At most 10 files");

  fireEvent.change(filesAtOnce, { target: { value: "2" } });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() => {
    expect(create).toHaveBeenCalledWith(
      expect.objectContaining({ name: "Kids", max_concurrent_files: 2 }),
    );
  });
});

it("saves the complete library contract without resetting hidden or advanced values", async () => {
  asOperator();
  const existing = library({
    include_patterns_csv: "*feature*",
    exclude_patterns_csv: "*sample*",
    max_file_size_mb: 80_000,
    rejected_file_action: "delete_file",
    created_after: "2026-01-01T00:00:00Z",
    created_before: "2027-01-01T00:00:00Z",
    modified_after: "2026-02-01T00:00:00Z",
    modified_before: "2026-12-01T00:00:00Z",
    sidecar_patterns_csv: ".srt,.nfo,.jpg",
    preserve_original_timestamps: true,
    output_collision_policy: "keep_both",
    hardware_decode_mode: "device",
    hardware_device: "qsv",
    hardware_disabled_vendors_csv: "nvidia",
    ffmpeg_strictness: "strict",
    max_attempts: 7,
    retry_backoff_seconds: 120,
    retry_execution_failures: false,
    retry_preflight_failures: true,
    failure_policy: "hold",
    schedule_enabled: false,
    schedule_hours_limited: true,
    schedule_days: "mon,tue",
    schedule_start: "01:00",
    schedule_end: "06:00",
    priority: 12,
  });
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([existing]);
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(existing);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() => {
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        include_patterns_csv: "*feature*",
        exclude_patterns_csv: "*sample*",
        max_file_size_mb: 80_000,
        rejected_file_action: "delete_file",
        created_after: "2026-01-01T00:00:00.000Z",
        created_before: "2027-01-01T00:00:00.000Z",
        modified_after: "2026-02-01T00:00:00.000Z",
        modified_before: "2026-12-01T00:00:00.000Z",
        sidecar_patterns_csv: ".srt,.nfo,.jpg",
        preserve_original_timestamps: true,
        output_collision_policy: "keep_both",
        hardware_decode_mode: "device",
        hardware_device: "qsv",
        hardware_disabled_vendors_csv: "nvidia",
        ffmpeg_strictness: "strict",
        max_attempts: 7,
        retry_backoff_seconds: 120,
        retry_execution_failures: false,
        retry_preflight_failures: true,
        failure_policy: "hold",
        schedule_enabled: false,
        schedule_hours_limited: true,
        schedule_days: "mon,tue",
        schedule_start: "01:00",
        schedule_end: "06:00",
        priority: 12,
      }),
    );
  });
});

it("offers Reject only when a linked manager can take one, and says why", async () => {
  asOperator();
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([
    library({ manager_connection_ids: [4] }),
  ]);
  const support = vi
    .spyOn(managersApi, "fetchProcessingRejectSupport")
    .mockResolvedValue({
      available: false,
      reason: "Deluno does not yet say it can replace a rejected release.",
    });

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));

  expect(
    await screen.findByText(
      "Reject: Deluno does not yet say it can replace a rejected release.",
    ),
  ).toBeInTheDocument();
  expect(support).toHaveBeenCalledWith([4]);
  const option = screen.getByRole("option", {
    name: "Reject the release so a different one is found",
  }) as HTMLOptionElement;
  expect(option.disabled).toBe(true);
});

it("lets an operator choose Reject when a linked manager supports it", async () => {
  asOperator();
  const existing = library({ manager_connection_ids: [2] });
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([existing]);
  vi.spyOn(managersApi, "fetchProcessingRejectSupport").mockResolvedValue({
    available: true,
    reason:
      "Radarr can remove the download, blocklist the release and search for another.",
  });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(existing);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  const option = (await screen.findByRole("option", {
    name: "Reject the release so a different one is found",
  })) as HTMLOptionElement;
  await waitFor(() => expect(option.disabled).toBe(false));
  fireEvent.change(option.closest("select") as HTMLSelectElement, {
    target: { value: "reject" },
  });
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ failure_policy: "reject" }),
    ),
  );
});

it("keeps the original download when told to, saves it, and asks the check about seeding with it", async () => {
  asOperator();
  const existing = library({ media_type: "tv", name: "TV" });
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([existing]);
  const check = vi
    .spyOn(managersApi, "fetchProcessingManagerSetup")
    .mockResolvedValue({ media_type: "tv", managers: [] });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(existing);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  const toggle = screen.getByRole("checkbox", {
    name: /After cleaning, remove the original download/,
  });
  expect(toggle).toBeChecked();
  expect(
    screen.getByText(
      "Turn off if your download client is still seeding it — Sonarr, Radarr or your client will clean it up.",
    ),
  ).toBeInTheDocument();

  fireEvent.click(toggle);
  await waitFor(() =>
    expect(check).toHaveBeenLastCalledWith(
      "tv",
      "/srv/movies/in",
      "/srv/movies/out",
      false,
    ),
  );
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() =>
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({ remove_original_after_success: false }),
    ),
  );
});

it("fills a library's folders from what Deluno reports, then saves them", async () => {
  asOperator();
  const existing = library({
    media_type: "tv",
    name: "TV",
    watched_folder: "/media/tv",
    output_folder: "",
  });
  vi.spyOn(api, "fetchProcessingLibraries").mockResolvedValue([existing]);
  vi.spyOn(managersApi, "fetchProcessingManagerSetup").mockResolvedValue({
    media_type: "tv",
    managers: [
      {
        connection_id: 5,
        kind: "deluno",
        name: "Deluno",
        label: "Deluno",
        flow: "handoff",
        ready: false,
        mapping: null,
        suggested_watched_folder: "/media/downloads/complete/tv",
        suggested_output_folder: "/media/downloads/weir/tv",
        lines: [],
      },
    ],
  });
  const update = vi
    .spyOn(api, "updateProcessingLibrary")
    .mockResolvedValue(existing);

  render(<LibrariesTab />, { wrapper });
  fireEvent.click(await screen.findByRole("button", { name: "Edit" }));
  fireEvent.click(
    await screen.findByRole("button", { name: "Use Deluno's folders" }),
  );
  fireEvent.click(screen.getByTestId("processing-library-save"));

  await waitFor(() => {
    expect(update).toHaveBeenCalledWith(
      1,
      expect.objectContaining({
        watched_folder: "/media/downloads/complete/tv",
        output_folder: "/media/downloads/weir/tv",
      }),
    );
  });
});
