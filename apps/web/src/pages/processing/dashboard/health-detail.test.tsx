import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { LibraryFolderChain } from "../../../lib/processing/library-folder-chain-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { CheckNow } from "./check-now";
import { HealthDetail } from "./health-detail";
import type { Health } from "./use-health";

const health: Health = {
  workflows: [],
  managers: [],
  downloadClients: [],
  tools: null,
  recheckFolders: vi.fn(),
};
const check: CheckNow = { run: vi.fn(), pending: false, notice: null };

vi.mock("./use-health", () => ({ useHealth: () => health }));
vi.mock("./check-now", () => ({
  useCheckNow: () => check,
  CheckNowButton: () => <button type="button">Check now</button>,
}));

const movies = {
  id: 2,
  name: "Movies",
  enabled: true,
  output_folder: "D:/clean/movies",
  minimum_free_disk_space_mb: 20_480,
  manager_connection_ids: [1],
} as ProcessingLibrary;

const chain: LibraryFolderChain = {
  library_id: 2,
  ready: false,
  local: {
    ready: true,
    lines: [{ state: "ok", text: "Weir can read the watched folder." }],
  },
  managers: [
    {
      connection_id: 1,
      kind: "radarr",
      name: "Radarr",
      label: "Radarr on MEDIA-PC",
      ready: false,
      lines: [{ state: "problem", text: "Radarr does not import from here." }],
    },
  ],
  download_clients: [
    {
      connection_id: 3,
      kind: "qbittorrent",
      name: "qBittorrent",
      label: "qBittorrent on NAS",
      ready: true,
      lines: [
        { state: "unverified", text: "Weir cannot see the client's folder." },
      ],
    },
  ],
} as LibraryFolderChain;

function renderDetail(workflows: ProcessingLibrary[] = [movies]) {
  render(
    <MemoryRouter>
      <HealthDetail workflows={workflows} />
    </MemoryRouter>,
  );
  return screen.getByTestId("health-detail");
}

beforeEach(() => {
  health.workflows = [
    {
      workflow: movies,
      verdict: { words: "Needs a fix", tone: "warning" },
      why: "Radarr does not import from here.",
      chain,
    },
  ];
  health.managers = [
    {
      id: 1,
      kind: "radarr",
      name: "Radarr on MEDIA-PC",
      enabled: true,
      base_url: "http://media-pc:7878",
      last_test_ok: true,
      last_test_at: new Date().toISOString(),
      last_test_detail: "Connected.",
    },
  ] as Health["managers"];
  health.downloadClients = [];
  health.tools = [
    {
      key: "ffmpeg",
      name: "FFmpeg",
      version: "7.1.1",
      banner: "ffmpeg version 7.1.1 Copyright",
      tone: "healthy",
    },
  ];
});

describe("the full Health view", () => {
  it("shows a workflow with its kind, its verdict, why, and its links", () => {
    renderDetail();

    const workflow = screen.getByTestId("health-workflow");
    expect(workflow).toHaveTextContent("Movies");
    expect(workflow).toHaveTextContent("Linked to Radarr on MEDIA-PC");
    expect(workflow).toHaveTextContent("Needs a fix");
    expect(
      within(workflow).getByRole("link", { name: "Open this workflow" }),
    ).toHaveAttribute("href", "/settings?tab=libraries&edit=2");
    expect(
      within(workflow).getByRole("link", { name: "Media managers" }),
    ).toHaveAttribute("href", "/settings?tab=media-managers");
  });

  it("groups the whole folder chain: Weir's own folders, each manager, each client", () => {
    renderDetail();

    const workflow = screen.getByTestId("health-workflow");
    expect(
      within(workflow).getByRole("region", { name: "Weir's own folders" }),
    ).toHaveTextContent("Weir can read the watched folder.");
    expect(
      within(workflow).getByRole("region", { name: "Radarr on MEDIA-PC" }),
    ).toHaveTextContent("Radarr does not import from here.");
    expect(
      within(workflow).getByRole("region", { name: "qBittorrent on NAS" }),
    ).toHaveTextContent("Weir cannot see the client's folder.");
  });

  it("says the folder check has not answered while a workflow has no chain yet", () => {
    health.workflows = [
      {
        workflow: movies,
        verdict: { words: "Checking…", tone: "neutral" },
        why: null,
        chain: undefined,
      },
    ];
    renderDetail();

    expect(screen.getByTestId("health-workflow")).toHaveTextContent(
      "The folder check has not answered yet.",
    );
  });

  it("lists the connections with their details", () => {
    renderDetail();

    const connections = screen.getByRole("region", { name: "Connections" });
    expect(connections).toHaveTextContent("Radarr on MEDIA-PC");
    expect(connections).toHaveTextContent("Media manager, Radarr");
    expect(connections).toHaveTextContent("http://media-pc:7878");
    expect(connections).toHaveTextContent("Connected.");
  });

  it("lists the tools with their versions and what each reported", () => {
    renderDetail();

    const tools = screen.getByRole("region", { name: "Tools" });
    expect(tools).toHaveTextContent("FFmpeg");
    expect(tools).toHaveTextContent("7.1.1");
    expect(tools).toHaveTextContent("ffmpeg version 7.1.1 Copyright");
  });

  it("says Weir does not report free space, and shows what it keeps free instead", () => {
    renderDetail();

    expect(screen.getByTestId("health-disk-note")).toHaveTextContent(
      "Weir does not report how much room is left on a drive.",
    );
    const disk = screen.getByRole("region", { name: "Disk space" });
    expect(disk).toHaveTextContent("Keeps 20.00 GB free");
    expect(disk).toHaveTextContent("D:/clean/movies");
  });

  it("notes the workflows that are switched off", () => {
    renderDetail([movies, { ...movies, id: 3, enabled: false }]);

    expect(screen.getByRole("region", { name: "Workflows" })).toHaveTextContent(
      "1 other is switched off",
    );
  });
});
