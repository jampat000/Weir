import { fireEvent, render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";

import type { LibraryFile } from "../../lib/processing/library-mode-api";
import { WithWorkflows } from "../../test/with-workflows";
import { LibraryTable } from "./library-table";

function file(path: string, overrides: Partial<LibraryFile> = {}): LibraryFile {
  return {
    path,
    size_bytes: 1_000_000,
    classification: "matches",
    status: "matches",
    status_reason: null,
    cleaned_at: null,
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

function renderSelectable(
  groups: [string, LibraryFile[]][],
  selected: string[] = [],
) {
  const onToggle = vi.fn();
  const onOpen = vi.fn();
  render(
    <WithWorkflows>
      <LibraryTable
        libraryName="TV"
        groups={groups}
        compact={false}
        openPath={null}
        selected={new Set(selected)}
        onToggle={onToggle}
        onOpen={onOpen}
      />
    </WithWorkflows>,
  );
  return { onToggle, onOpen };
}

const episodes: [string, LibraryFile[]][] = [
  [
    "Northbound",
    [
      file("Northbound/S01E01.mkv", {
        classification: "would_change",
        status: "needs_cleaning",
        manager_kind: "sonarr",
      }),
      file("Northbound/S01E02.mkv", {
        classification: "would_change",
        status: "needs_cleaning",
        manager_kind: "sonarr",
      }),
      file("Northbound/S01E03.mkv", { manager_kind: "sonarr" }),
    ],
  ],
];

it("gives a title with one file a single row that carries the title, the file and its source", () => {
  const { onToggle, onOpen } = renderSelectable([
    [
      "Detour",
      [
        file("Detour (1945)/Detour (1945).mkv", {
          classification: "would_change",
          status: "needs_cleaning",
          manager_kind: "radarr",
        }),
      ],
    ],
  ]);

  const rows = screen.getAllByRole("row").slice(1);
  expect(rows).toHaveLength(1);
  expect(rows[0]).toHaveTextContent("Detour");
  expect(rows[0]).toHaveTextContent("Detour (1945).mkv");
  expect(rows[0]).toHaveTextContent("Radarr");

  fireEvent.click(screen.getByRole("checkbox", { name: "Select Detour" }));
  expect(onToggle).toHaveBeenCalledWith("Detour (1945)/Detour (1945).mkv");

  fireEvent.click(within(rows[0]).getByRole("button"));
  expect(onOpen).toHaveBeenCalledWith("Detour (1945)/Detour (1945).mkv");
});

it("nests a title's files under it, with its source and file count on the title", () => {
  renderSelectable(episodes);

  expect(screen.getByText("Sonarr · 3 files")).toBeVisible();
  expect(screen.getByText("2 need cleaning")).toBeVisible();
  expect(screen.getAllByTestId("library-row")).toHaveLength(3);
  expect(
    screen.getByRole("checkbox", { name: "Select S01E01.mkv" }),
  ).toBeEnabled();
  expect(
    screen.getByRole("checkbox", { name: "Select S01E03.mkv" }),
  ).toBeDisabled();
});

it("selects every file Weir would change when the title's box is ticked", () => {
  const { onToggle } = renderSelectable(episodes);

  fireEvent.click(
    screen.getByRole("checkbox", { name: "Select all 3 files of Northbound" }),
  );

  expect(onToggle.mock.calls.map(([path]) => path)).toEqual([
    "Northbound/S01E01.mkv",
    "Northbound/S01E02.mkv",
  ]);
});

it("shows the title's box half ticked while some of its files are selected, and finishes the job when pressed", () => {
  const { onToggle } = renderSelectable(episodes, ["Northbound/S01E01.mkv"]);
  const box = screen.getByRole("checkbox", {
    name: "Select all 3 files of Northbound",
  });

  expect(box).toHaveProperty("indeterminate", true);
  fireEvent.click(box);
  expect(onToggle.mock.calls.map(([path]) => path)).toEqual([
    "Northbound/S01E02.mkv",
  ]);
});

it("clears the title's files when every one is already selected", () => {
  const { onToggle } = renderSelectable(episodes, [
    "Northbound/S01E01.mkv",
    "Northbound/S01E02.mkv",
  ]);
  const box = screen.getByRole("checkbox", {
    name: "Select all 3 files of Northbound",
  });

  expect(box).toBeChecked();
  fireEvent.click(box);
  expect(onToggle).toHaveBeenCalledTimes(2);
});

it("offers no box to select a title none of whose files Weir would change", () => {
  renderSelectable([
    ["Northbound", [file("Northbound/a.mkv"), file("Northbound/b.mkv")]],
  ]);

  expect(
    screen.getByRole("checkbox", { name: "Select all 2 files of Northbound" }),
  ).toBeDisabled();
});

it("folds a title's files away and brings them back", () => {
  renderSelectable(episodes);
  const fold = screen.getByRole("button", { name: /Northbound/ });

  expect(fold).toHaveAttribute("aria-expanded", "true");
  fireEvent.click(fold);
  expect(fold).toHaveAttribute("aria-expanded", "false");
  expect(screen.queryAllByTestId("library-row")).toHaveLength(0);
  fireEvent.click(fold);
  expect(screen.getAllByTestId("library-row")).toHaveLength(3);
});
