import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen, within } from "@testing-library/react";
import type { ReactNode } from "react";
import { afterEach, expect, it, vi } from "vitest";

import * as downloadClientsApi from "../../../../lib/download-clients/download-clients-api";
import type { DownloadClientSuggestion } from "../../../../lib/download-clients/download-clients-api";
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

function setup(
  managers: ProcessingManagerSetupItem[],
  downloadClients: DownloadClientSuggestion[] = [],
) {
  vi.spyOn(downloadClientsApi, "fetchDownloadClientSuggestions").mockResolvedValue(
    downloadClients,
  );
  return vi
    .spyOn(managersApi, "fetchProcessingManagerSetup")
    .mockResolvedValue({ media_type: "tv", managers });
}

afterEach(() => {
  vi.restoreAllMocks();
});

it("shows Sonarr exactly what to enter, with copy buttons, and what is still wrong", async () => {
  const fetch = setup([sonarr]);
  const writeText = vi.fn().mockResolvedValue(undefined);
  Object.assign(navigator, { clipboard: { writeText } });

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete"
      outputFolder="/media/downloads/weir"
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
  expect(within(block).getByText("Needs attention")).toBeInTheDocument();
  expect(
    within(block).getByText(
      "Sonarr has no remote path mapping for /media/downloads/complete yet — add the one above.",
    ),
  ).toBeInTheDocument();

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

it("tells a Deluno library there is nothing to map and offers Deluno's own folders", async () => {
  setup([deluno]);
  const onUseFolders = vi.fn();

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/tv"
      outputFolder=""
      editable
      onUseFolders={onUseFolders}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Deluno" });
  expect(
    within(block).getByText(/so there is nothing to map/),
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

it("does not offer Deluno's folders once the library already uses them", async () => {
  setup([{ ...deluno, ready: true, lines: [] }]);

  render(
    <LibraryManagerSetup
      mediaType="tv"
      watchedFolder="/media/downloads/complete/tv"
      outputFolder="/media/downloads/weir/tv"
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  const block = await screen.findByRole("region", { name: "Deluno" });
  expect(within(block).getByText("Ready")).toBeInTheDocument();
  expect(
    within(block).queryByRole("button", { name: "Use Deluno's folders" }),
  ).not.toBeInTheDocument();
});

it("says how to get help when no media manager covers the library", async () => {
  setup([]);

  render(
    <LibraryManagerSetup
      mediaType="movie"
      watchedFolder="/in"
      outputFolder="/out"
      editable
      onUseFolders={() => {}}
    />,
    { wrapper },
  );

  expect(
    await screen.findByText(
      /No Sonarr, Radarr or Deluno connection covers Movies/,
    ),
  ).toBeInTheDocument();
});

it("offers a bare download client's own folder as a suggested watched folder, with no manager connected", async () => {
  const onUseFolders = vi.fn();
  const sabnzbd: DownloadClientSuggestion = {
    connection_id: 9,
    kind: "sabnzbd",
    name: "SABnzbd",
    label: "SABnzbd",
    flow: "download_client",
    ready: true,
    lines: [
      { state: "ok", text: "SABnzbd's default completed-downloads folder is /downloads/complete." },
    ],
    suggested_watched_folder: "/downloads/complete",
    category_folders: [],
  };
  setup([], [sabnzbd]);

  render(
    <LibraryManagerSetup
      mediaType="movie"
      watchedFolder="/media/movies"
      outputFolder="/media/movies-out"
      editable
      onUseFolders={onUseFolders}
    />,
    { wrapper },
  );

  expect(
    screen.queryByText(/No Sonarr, Radarr or Deluno connection covers/),
  ).not.toBeInTheDocument();
  const block = await screen.findByRole("region", { name: "SABnzbd" });
  expect(within(block).getByText("/downloads/complete")).toBeInTheDocument();

  fireEvent.click(
    within(block).getByRole("button", {
      name: "Use this as the watched folder",
    }),
  );
  expect(onUseFolders).toHaveBeenCalledWith("/downloads/complete", null);
});
