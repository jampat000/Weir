import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type {
  ProcessingFile,
  ProcessingFileRemoveOptions,
} from "../../lib/processing/files-api";
import { HistoryFileActions } from "./history-file-actions";

const forget = vi.fn().mockResolvedValue(undefined);
const removeOptions = vi.fn<() => Promise<ProcessingFileRemoveOptions>>();

vi.mock("../../lib/processing/files-queries", () => {
  const mutation = (fn = vi.fn()) => ({
    mutateAsync: fn,
    isPending: false,
  });
  return {
    useForgetProcessingFile: () => mutation(forget),
    useProcessingFileRemoveOptions: () => mutation(removeOptions),
    useMoveProcessingFileToTop: () => mutation(),
    useRequeueProcessingFile: () => mutation(),
    useProcessingWhyHeld: () => mutation(),
    useProcessProcessingFileNow: () => mutation(),
    useProcessingCheckLibraryAgain: () => mutation(),
    useProcessingFileTracks: () => mutation(),
    useSubmitProcessingManualPlan: () => mutation(),
  };
});
vi.mock("../../lib/processing/libraries-queries", () => ({
  useProcessingLibrariesQuery: () => ({
    data: [{ id: 1, name: "Movies", media_type: "movie" }],
  }),
}));

function file(partial: Partial<ProcessingFile> = {}): ProcessingFile {
  return {
    id: 7,
    library_id: 1,
    library_name: "Movies",
    relative_path: "Film (2024)/film.mkv",
    status: "processing_failed",
    status_reason: "ffmpeg could not read the audio track.",
    blocked_by_connection: null,
    size_bytes: 4_000_000_000,
    failure_class: "execution",
    failure_attempts: 1,
    next_retry_at: null,
    output_collision_policy: null,
    output_collision_action: null,
    output_collision_reason: null,
    video_width: null,
    video_height: null,
    video_codec: null,
    audio_track_count: null,
    subtitle_track_count: null,
    duration_seconds: null,
    direct_play: [],
    progress_percent: null,
    progress_message: null,
    progress_eta_seconds: null,
    hold_until: null,
    size_changed_at: null,
    created_at: "2026-09-01T00:00:00Z",
    updated_at: "2026-09-01T00:00:00Z",
    last_seen_at: null,
    last_attempt_at: null,
    ...partial,
  };
}

function renderActions(partial: Partial<ProcessingFile> = {}) {
  render(
    <HistoryFileActions file={file(partial)} editable onRemoved={vi.fn()} />,
  );
}

describe("HistoryFileActions remove dialog", () => {
  beforeEach(() => {
    forget.mockReset().mockResolvedValue(undefined);
    removeOptions.mockReset();
  });

  it("removes a plain title immediately, with no dialog, when it does not qualify for a choice", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: false,
      manager_label: null,
      delete_handled_by_manager: false,
      keep_notifies_manager: false,
    });
    renderActions({ status: "processed" });

    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));

    await waitFor(() => expect(forget).toHaveBeenCalledWith({ id: 7 }));
    expect(
      screen.queryByTestId("history-remove-dialog"),
    ).not.toBeInTheDocument();
  });

  it("opens the dialog naming Weir alone when no manager is linked", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: true,
      manager_label: null,
      delete_handled_by_manager: false,
      keep_notifies_manager: false,
    });
    renderActions();

    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));

    expect(
      await screen.findByTestId("history-remove-dialog"),
    ).toBeInTheDocument();
    expect(screen.getByText("Delete the file")).toBeInTheDocument();
    expect(
      screen.getByText("Weir alone: the file is deleted."),
    ).toBeInTheDocument();
    expect(
      screen.getByText("Weir leaves it until it changes."),
    ).toBeInTheDocument();
  });

  it("names the manager that will delete the download and search again", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: true,
      manager_label: "Radarr",
      delete_handled_by_manager: true,
      keep_notifies_manager: false,
    });
    renderActions();

    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));

    expect(
      await screen.findByText(
        "Delete the download and ask Radarr for another copy",
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(
        "Radarr removes it, blocks this release and searches again.",
      ),
    ).toBeInTheDocument();
  });

  it("says the manager will be told the file will not be imported when keep notifies it", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: true,
      manager_label: "Deluno",
      delete_handled_by_manager: true,
      keep_notifies_manager: true,
    });
    renderActions();

    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));

    expect(
      await screen.findByText(
        "Weir leaves it until it changes. Deluno is told it won't be imported.",
      ),
    ).toBeInTheDocument();
  });

  it("sends the chosen resolution when a choice other than delete is confirmed", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: true,
      manager_label: null,
      delete_handled_by_manager: false,
      keep_notifies_manager: false,
    });
    renderActions();
    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));
    fireEvent.click(
      await screen.findByTestId("history-remove-dialog-choice-keep"),
    );

    fireEvent.click(screen.getByTestId("history-remove-dialog-confirm"));

    await waitFor(() =>
      expect(forget).toHaveBeenCalledWith({ id: 7, resolution: "keep" }),
    );
  });

  it("shows the server's refusal in the dialog and keeps it open when the action fails", async () => {
    removeOptions.mockResolvedValue({
      requires_choice: true,
      manager_label: "Radarr",
      delete_handled_by_manager: true,
      keep_notifies_manager: false,
    });
    forget.mockRejectedValueOnce(
      new Error("Radarr did not accept the rejection, so nothing was removed."),
    );
    renderActions();
    fireEvent.click(screen.getByRole("button", { name: "Remove from list" }));
    await screen.findByTestId("history-remove-dialog");

    fireEvent.click(screen.getByTestId("history-remove-dialog-confirm"));

    expect(
      await screen.findByText(
        "Radarr did not accept the rejection, so nothing was removed.",
      ),
    ).toBeInTheDocument();
    expect(screen.getByTestId("history-remove-dialog")).toBeInTheDocument();
  });
});
