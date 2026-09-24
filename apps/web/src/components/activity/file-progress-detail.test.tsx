import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";

import { FileProgressDetail } from "./file-progress-detail";

describe("FileProgressDetail", () => {
  it("renders a running pass in plain language", () => {
    const detail = JSON.stringify({
      status: "processing",
      relative_media_path: "movies/Caddyshack.mkv",
      percent: 42.4,
      eta_seconds: 71,
      elapsed_seconds: 50,
      processed_seconds: 1200,
      duration_seconds: 2800,
      speed: "16x",
      message: "Weir is writing the cleaned-up file.",
    });
    render(<FileProgressDetail detail={detail} />);
    expect(
      screen.getByTestId("processing-processing-progress-detail"),
    ).toBeInTheDocument();
    expect(screen.getByText("Processing")).toBeInTheDocument();
    expect(screen.getByText("42%")).toBeInTheDocument();
    expect(screen.getByRole("progressbar")).toHaveAttribute(
      "aria-valuenow",
      "42",
    );
    expect(screen.getByText(/About 1m 11s left/i)).toBeInTheDocument();
    expect(
      screen.getByText(/Processing speed 16x realtime/i),
    ).toBeInTheDocument();
  });

  it("uses finished styling and copy when the pass completes", () => {
    const detail = JSON.stringify({
      status: "finished",
      relative_media_path: "movies/Mickey 17.mkv",
      percent: 100,
      eta_seconds: 0,
      elapsed_seconds: 183,
      message: "Weir is writing the cleaned-up file.",
    });
    render(<FileProgressDetail detail={detail} />);
    const card = screen.getByTestId("processing-processing-progress-detail");

    expect(card).toHaveClass("mm-activity-processing--finished");
    expect(screen.getAllByText("Finished")).toHaveLength(2);
    expect(
      screen.getByText("Weir finished processing this file."),
    ).toBeInTheDocument();
    expect(screen.getByText("100%")).toBeInTheDocument();
    expect(screen.queryByText(/About 0s left/i)).not.toBeInTheDocument();
  });

  it("uses failed styling when the pass stops", () => {
    const detail = JSON.stringify({
      status: "failed",
      relative_media_path: "movies/Broken.mkv",
      percent: 63,
      reason: "ffmpeg stopped unexpectedly.",
    });
    render(<FileProgressDetail detail={detail} />);

    expect(
      screen.getByTestId("processing-processing-progress-detail"),
    ).toHaveClass("mm-activity-processing--failed");
    expect(screen.getAllByText("Stopped")).toHaveLength(2);
    expect(
      screen.getByText("ffmpeg stopped unexpectedly."),
    ).toBeInTheDocument();
  });

  it("shows plain text as it came", () => {
    render(<FileProgressDetail detail="Queued behind two files" />);
    expect(screen.getByText("Queued behind two files")).toHaveClass(
      "mm-activity-processing__raw",
    );
  });
});
