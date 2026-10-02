import { render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";

import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { WithWorkflows } from "../../test/with-workflows";
import { LibraryTable } from "./library-table";

function file(path: string, overrides: Partial<LibraryFile> = {}): LibraryFile {
  return {
    path,
    size_bytes: 1_000_000,
    classification: "matches",
    estimated_bytes_saved: 0,
    audio_track_count: 1,
    subtitle_track_count: 1,
    ...overrides,
  } as LibraryFile;
}

function renderTable(groups: [string, LibraryFile[]][]) {
  render(
    <WithWorkflows>
      <LibraryTable
        libraryName="Movies"
        groups={groups}
        compact={false}
        openPath={null}
        selected={new Set()}
        onToggle={vi.fn()}
        onOpen={vi.fn()}
      />
    </WithWorkflows>,
  );
}

it("shows a title's poster on its group, whichever of its files carries the address", () => {
  renderTable([
    [
      "Detour",
      [
        file("Detour (1945)/a.mkv", { poster_url: null }),
        file("Detour (1945)/b.mkv", {
          poster_url: "/api/v1/artwork/posters/movie-detour-1945",
        }),
      ],
    ],
  ]);

  expect(screen.getByRole("img", { name: "Detour" })).toHaveAttribute(
    "src",
    "/api/v1/artwork/posters/movie-detour-1945",
  );
});

it("shows the initials on a group none of whose files has a poster", () => {
  renderTable([
    ["Detour", [file("Detour (1945)/a.mkv", { poster_url: null })]],
  ]);

  const group = screen.getAllByRole("rowgroup")[0];
  expect(within(group).queryByRole("img")).toBeNull();
  expect(within(group).getByText("D")).toBeInTheDocument();
});

it("tints a group's tile for the library's place in Settings", () => {
  renderTable([
    ["Detour", [file("Detour (1945)/a.mkv", { poster_url: null })]],
  ]);

  const [group] = screen.getAllByRole("rowgroup");
  expect(
    (group.querySelector(".mm-tile") as HTMLElement).style.getPropertyValue(
      "--tile-hue",
    ),
  ).toBe("205");
});
