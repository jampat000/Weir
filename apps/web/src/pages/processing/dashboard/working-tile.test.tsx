import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { describe, expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../../lib/processing/files-api";
import type { WorkingItem } from "../processing-model";
import { WorkingTile } from "./working-tile";

const file = { id: 5 } as ProcessingFile;

function item(overrides: Partial<WorkingItem>): WorkingItem {
  return {
    key: "file-5",
    source: "download",
    name: "Glass Orchard S01E04",
    path: "Glass.Orchard.S01E04.mkv",
    facts: "",
    libraryName: "TV",
    step: "write",
    percent: 46.4,
    etaSeconds: null,
    speed: null,
    removedAudio: 0,
    removedSubtitles: 0,
    file,
    ...overrides,
  };
}

function renderTile(
  props: Partial<React.ComponentProps<typeof WorkingTile>> = {},
) {
  const onOpen = vi.fn();
  render(
    <MemoryRouter>
      <WorkingTile
        working={[]}
        filesAtOnce={2}
        waitSeconds={60}
        onOpen={onOpen}
        {...props}
      />
    </MemoryRouter>,
  );
  return { onOpen };
}

describe("the Working on now tile", () => {
  it("says how many are being worked on out of how many may be at once", () => {
    renderTile({ working: [item({})] });

    expect(screen.getByTestId("live-working-count")).toHaveTextContent("1");
    expect(
      screen.getByRole("region", { name: "Working on now" }),
    ).toHaveTextContent("of 2 at once");
  });

  it("says only 'at once' while the limit is not known", () => {
    renderTile({ filesAtOnce: null });

    expect(
      screen.getByRole("region", { name: "Working on now" }),
    ).toHaveTextContent("0at once");
  });

  it("shows each file with its step and its percent, rounded", () => {
    renderTile({
      working: [
        item({}),
        item({
          key: "file-6",
          name: "Sintel (2010)",
          step: "verify",
          percent: 100,
        }),
      ],
    });

    const rows = within(screen.getByTestId("live-working")).getAllByRole(
      "listitem",
    );
    expect(rows[0]).toHaveTextContent("Glass Orchard S01E04 · Writing46%");
    expect(rows[1]).toHaveTextContent("Sintel (2010) · Verifying100%");
  });

  it("shows a step with no percent yet without one", () => {
    renderTile({ working: [item({ step: "checking", percent: null })] });

    expect(screen.getByTestId("live-working")).toHaveTextContent(
      "Glass Orchard S01E04 · Checking",
    );
    expect(screen.getByTestId("live-working")).not.toHaveTextContent("%");
  });

  it("opens a file's story from its row", () => {
    const { onOpen } = renderTile({ working: [item({})] });

    fireEvent.click(screen.getByRole("button"));

    expect(onOpen).toHaveBeenCalledWith(file);
  });

  it("shows a library clean, which has no story, as a row that is not a button", () => {
    renderTile({ working: [item({ file: null, source: "library" })] });

    expect(screen.queryByRole("button")).toBeNull();
    expect(screen.getByTestId("live-working")).toHaveTextContent(
      "Glass Orchard S01E04",
    );
  });

  it("says nothing is being cleaned when nothing is", () => {
    renderTile();

    expect(
      screen.getByText("Nothing is being cleaned right now."),
    ).toBeInTheDocument();
    expect(screen.queryByTestId("live-working")).toBeNull();
  });

  it("names the wait for a new download beside the link that changes it", () => {
    renderTile();

    const tile = screen.getByRole("region", { name: "Working on now" });
    expect(tile).toHaveTextContent("new downloads wait 60s");
    expect(within(tile).getByRole("link", { name: "Change" })).toHaveAttribute(
      "href",
      "/settings?tab=performance",
    );
  });

  it("keeps the link when the workflows disagree about the wait", () => {
    renderTile({ waitSeconds: null });

    const tile = screen.getByRole("region", { name: "Working on now" });
    expect(tile).not.toHaveTextContent("new downloads wait");
    expect(
      within(tile).getByRole("link", { name: "Change" }),
    ).toBeInTheDocument();
  });
});
