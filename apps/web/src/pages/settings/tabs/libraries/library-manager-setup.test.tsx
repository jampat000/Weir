import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as managersApi from "../../../../lib/processing/library-managers-api";
import type { ProcessingManagerSetupItem } from "../../../../lib/processing/library-managers-api";
import { LibraryManagerSetup } from "./library-manager-setup";

function wrapper({ children }: { children: ReactNode }) {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

const sonarr: ProcessingManagerSetupItem = {
  connection_id: 3,
  kind: "sonarr",
  name: "Sonarr",
  label: "Sonarr",
  flow: "remote_path_mapping",
  ready: false,
  mapping: {
    hosts: ["qbittorrent"],
    remote_path: "/media/downloads/complete",
    local_path: "/media/downloads/weir",
  },
  lines: [
    { state: "ok", text: "Completed Download Handling is on in Sonarr." },
    {
      state: "problem",
      text: "Sonarr has no remote path mapping for /media/downloads/complete yet — add the one above.",
    },
  ],
};

const deluno: ProcessingManagerSetupItem = {
  connection_id: 5,
  kind: "deluno",
  name: "Deluno",
  label: "Deluno",
  flow: "handoff",
  ready: false,
  mapping: null,
  suggested_watched_folder: "/media/downloads/complete/tv",
  suggested_output_folder: "/media/downloads/weir/tv",
  lines: [
    {
      state: "problem",
      text: "TV's downloads arrive in /media/downloads/complete/tv, which is not inside the watched folder, so Weir would refuse its hand-offs. Use Deluno's folders.",
    },
  ],
};

function setup(managers: ProcessingManagerSetupItem[]) {
  return vi
    .spyOn(managersApi, "fetchProcessingManagerSetup")
    .mockResolvedValue({ media_type: "tv", managers });
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows Sonarr exactly what to enter, with copy buttons, and leaves what is still wrong to the folder chain", async () => {
  const fetch = setup([sonarr]);
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.assign(navigator, { clipboard: { writeText } });

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete"
      outputFolder="/media/downloads/weir"
      linkedConnectionIds={[3]}
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Sonarr" });
  expect(
    within(block).getByText(
      /Settings → Download Clients → Remote Path Mappings/,
    ),
  ).toBeInTheDocument();
  const table = within(block).getByRole("table");
  expect(within(table).getByText("qbittorrent")).toBeInTheDocument();
  expect(
    within(table).getByText("/media/downloads/complete"),
  ).toBeInTheDocument();
  expect(within(table).getByText("/media/downloads/weir")).toBeInTheDocument();
  // Whether the mapping is right is the folder chain's to say, once, in its own section.
  expect(within(block).queryByText("Needs attention")).not.toBeInTheDocument();
  expect(within(block).queryByText(/Needs a fix/)).not.toBeInTheDocument();

  fireEvent.click(
    within(block).getByRole("button", { name: "Copy Sonarr Local Path" }),
  );
  expect(writeText).toHaveBeenCalledWith("/media/downloads/weir");
  expect(await within(block).findByText("Copied")).toBeInTheDocument();
  expect(fetch).toHaveBeenCalledWith(
    "tv",
    "/media/downloads/complete",
    "/media/downloads/weir",
    true,
    [3],
  );
});

it("offers a Sonarr download client's own folder as a suggested watched folder", async () => {
  const onUseFolders = vi.fn();
  setup([{ ...sonarr, suggested_watched_folder: "/downloads/tv-sonarr" }]);

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete"
      outputFolder="/media/downloads/weir"
      linkedConnectionIds={[3]}
      editable
      onUseFolders={onUseFolders}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Sonarr" });
  expect(within(block).getByText("/downloads/tv-sonarr")).toBeInTheDocument();

  fireEvent.click(
    within(block).getByRole("button", {
      name: "Use this as the watched folder",
    }),
  );
  expect(onUseFolders).toHaveBeenCalledWith("/downloads/tv-sonarr", null);
});

it("does not suggest a Sonarr download client folder that is already the watched folder", async () => {
  setup([{ ...sonarr, suggested_watched_folder: "/media/downloads/complete" }]);

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete"
      outputFolder="/media/downloads/weir"
      linkedConnectionIds={[3]}
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Sonarr" });
  expect(
    within(block).queryByRole("button", {
      name: "Use this as the watched folder",
    }),
  ).not.toBeInTheDocument();
});

it("tells a Deluno workflow there is nothing to map and offers Deluno's own folders", async () => {
  setup([deluno]);
  const onUseFolders = vi.fn();

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/tv"
      outputFolder=""
      linkedConnectionIds={[5]}
      editable
      onUseFolders={onUseFolders}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Deluno" });
  expect(
    within(block).getByText(/Deluno reports downloads in/),
  ).toBeInTheDocument();
  expect(within(block).queryByRole("table")).not.toBeInTheDocument();
  expect(
    within(block).queryByText(/Remote Path Mappings/),
  ).not.toBeInTheDocument();

  fireEvent.click(
    within(block).getByRole("button", { name: "Use Deluno's folders" }),
  );
  expect(onUseFolders).toHaveBeenCalledWith(
    "/media/downloads/complete/tv",
    "/media/downloads/weir/tv",
  );
});

it("does not offer Deluno's folders once the workflow already uses them", async () => {
  setup([{ ...deluno, ready: true, lines: [] }]);

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete/tv"
      outputFolder="/media/downloads/weir/tv"
      linkedConnectionIds={[5]}
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Deluno" });
  expect(
    within(block).queryByRole("button", { name: "Use Deluno's folders" }),
  ).not.toBeInTheDocument();
});

it("reads nothing while the workflow is linked to no media manager", async () => {
  const fetch = setup([sonarr]);

  render(
    <LibraryManagerSetup
      mediaType="movie"
      watchedFolder="/in"
      outputFolder="/out"
      linkedConnectionIds={[]}
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  expect(
    await screen.findByTestId("library-manager-setup"),
  ).toBeInTheDocument();
  expect(fetch).not.toHaveBeenCalled();
});
