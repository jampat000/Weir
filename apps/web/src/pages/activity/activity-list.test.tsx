import { render, screen, within } from "@testing-library/react";
import { expect, it, vi } from "vitest";

import type { ProcessingFile } from "../../lib/processing/files-api";
import type { LibraryClean } from "../../lib/processing/library-cleans-api";
import { WithWorkflows } from "../../test/with-workflows";
import { cleanEntry, downloadEntry } from "./activity-entries";
import { ActivityList } from "./activity-list";

const NOW = Date.parse("2026-10-02T10:00:00Z");

const download = {
  id: 1,
  library_id: 2,
  library_name: "TV",
  relative_path: "The.Quiet.Harbour.S01E01.1080p.WEB-DL.mkv",
  status: "processed",
  size_bytes: 1_000_000,
  created_at: "2026-10-02T09:50:00",
  updated_at: "2026-10-02T09:59:00",
  poster_url: "/api/v1/artwork/posters/tv-the-quiet-harbour-2019",
} as ProcessingFile;

const clean = {
  id: 7,
  library_id: 1,
  library_name: "Movies",
  relative_path: "D:/Media/Movies/Detour (1945)/Detour (1945).mkv",
  outcome: "cleaned",
  recorded_at: "2026-10-02T09:58:00",
} as LibraryClean;

function renderList() {
  render(
    <WithWorkflows>
      <ActivityList
        entries={[downloadEntry(download), cleanEntry(clean)]}
        selectedKey={null}
        now={NOW}
        onPick={vi.fn()}
      />
    </WithWorkflows>,
  );
}

it("shows the title's small poster at the start of a download's row", () => {
  renderList();

  const [row] = screen.getAllByRole("row").slice(1);
  expect(within(row).getByRole("img")).toHaveAttribute(
    "src",
    "/api/v1/artwork/posters/tv-the-quiet-harbour-2019",
  );
});

it("shows the initials for a clean in a library, which has no poster of its own", () => {
  renderList();

  const [, row] = screen.getAllByRole("row").slice(1);
  expect(within(row).queryByRole("img")).toBeNull();
  expect(within(row).getByText("D")).toBeInTheDocument();
});

it("tints each row's tile for its workflow's place in Settings", () => {
  renderList();

  const [download, clean] = screen.getAllByRole("row").slice(1);
  expect(
    (download.querySelector(".mm-tile") as HTMLElement).style.getPropertyValue(
      "--tile-hue",
    ),
  ).toBe("265");
  expect(
    (clean.querySelector(".mm-tile") as HTMLElement).style.getPropertyValue(
      "--tile-hue",
    ),
  ).toBe("205");
});
