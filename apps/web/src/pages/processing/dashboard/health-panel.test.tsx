import { fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { CheckNow } from "./check-now";
import { HealthPanel } from "./health-panel";
import type { Health } from "./use-health";

const NOW = Date.parse("2026-10-02T12:00:00Z");

const health: Health = {
  workflows: [],
  managers: [],
  downloadClients: [],
  tools: null,
  recheckFolders: vi.fn(),
};
const check: CheckNow = { run: vi.fn(), pending: false, notice: null };
const useHealth = vi.fn<
  (workflows: unknown, workflowId: number | null | undefined) => Health
>(() => health);

vi.mock("./use-health", () => ({
  useHealth: (workflows: unknown, workflowId: number | null | undefined) =>
    useHealth(workflows, workflowId),
}));
vi.mock("./check-now", () => ({
  useCheckNow: () => check,
  CheckNowButton: ({ check: current }: { check: CheckNow }) => (
    <button type="button" onClick={current.run} disabled={current.pending}>
      {current.pending ? "Checking…" : "Check now"}
    </button>
  ),
}));

const movies = { id: 2, name: "Movies" } as ProcessingLibrary;

function renderPanel(workflowId?: number | null, route = "/") {
  render(
    <MemoryRouter initialEntries={[route]}>
      <HealthPanel workflows={[movies]} workflowId={workflowId} />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Health" });
}

const secondsBefore = (seconds: number) =>
  new Date(NOW - seconds * 1000).toISOString();

beforeEach(() => {
  vi.useFakeTimers({ toFake: ["Date"] });
  vi.setSystemTime(NOW);
  health.workflows = [];
  health.managers = [];
  health.downloadClients = [];
  health.tools = null;
  check.pending = false;
  check.notice = null;
  check.run = vi.fn();
  useHealth.mockClear();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("the Health panel", () => {
  it("lists each workflow with how it is linked and its folder-chain verdict, linked to the workflow", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "In sync", tone: "healthy" },
        why: null,
        chain: undefined,
      },
    ];
    const panel = renderPanel();

    const link = within(panel).getByRole("link", { name: /Movies/ });
    expect(link).toHaveAttribute("href", "/settings?tab=libraries&edit=2");
    expect(link).toHaveTextContent("Weir only");
    expect(panel).toHaveTextContent("In sync");
  });

  it("says in one line why a workflow is not in sync", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "Needs a fix", tone: "warning" },
        why: "The output folder is missing.",
        chain: undefined,
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Needs a fix");
    expect(panel).toHaveTextContent("The output folder is missing.");
  });

  it("names the manager a linked workflow is linked to", () => {
    health.managers = [
      { id: 1, kind: "radarr", name: "Radarr on MEDIA-PC", enabled: true },
    ] as Health["managers"];
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [1] },
        verdict: { words: "In sync", tone: "healthy" },
        why: null,
        chain: undefined,
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Linked to Radarr on MEDIA-PC");
  });

  it("says no workflow is on, rather than leaving the section empty", () => {
    const panel = renderPanel();

    expect(panel).toHaveTextContent("No workflow is switched on.");
  });

  it("shows each connection with how long ago it answered, or that it does not", () => {
    health.managers = [
      {
        id: 1,
        kind: "radarr",
        name: "Radarr",
        enabled: true,
        last_test_ok: true,
        last_test_at: secondsBefore(12),
      },
    ] as Health["managers"];
    health.downloadClients = [
      {
        id: 1,
        kind: "qbittorrent",
        name: "qBittorrent",
        enabled: true,
        last_test_ok: false,
      } as DownloadClientConnection,
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Radarr answered 12s ago");
    expect(panel).toHaveTextContent("qBittorrent not answering");
  });

  it("counts what to look at beside its title, and says all clear when nothing is", () => {
    expect(renderPanel()).toHaveTextContent("all clear");
  });

  it("counts a workflow that needs a fix and a connection that does not answer", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "Needs a fix", tone: "warning" },
        why: "It is missing.",
        chain: undefined,
      },
    ];
    health.downloadClients = [
      {
        id: 1,
        kind: "qbittorrent",
        name: "qBittorrent",
        enabled: true,
        last_test_ok: false,
      } as DownloadClientConnection,
    ];

    expect(renderPanel()).toHaveTextContent("2 to look at");
  });

  it("shows the tools' versions, or says it is reading them", () => {
    expect(renderPanel()).toHaveTextContent("Reading the tools…");
  });

  it("lists FFmpeg and mkvmerge with their versions", () => {
    health.tools = [
      {
        key: "ffmpeg",
        name: "FFmpeg",
        version: "7.1.1",
        banner: "",
        tone: "healthy",
      },
      {
        key: "mkvmerge",
        name: "mkvmerge",
        version: "89.0.0",
        banner: "",
        tone: "healthy",
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("FFmpeg 7.1.1");
    expect(panel).toHaveTextContent("mkvmerge 89.0.0");
  });

  it("narrows to the workflow it is given", () => {
    renderPanel(2);

    expect(useHealth).toHaveBeenCalledWith([movies], 2);
  });
});

describe("the Health panel's two actions", () => {
  it("runs the checks again when Check now is pressed", () => {
    renderPanel();

    fireEvent.click(screen.getByRole("button", { name: "Check now" }));

    expect(check.run).toHaveBeenCalledTimes(1);
  });

  it("says what the last check found", () => {
    check.notice = "Folders and 2 connections checked: all answered.";
    const panel = renderPanel();

    expect(within(panel).getByRole("status")).toHaveTextContent(
      "Folders and 2 connections checked: all answered.",
    );
  });

  it("opens the dashboard's System view with Full detail, keeping the chosen workflow", () => {
    const panel = renderPanel(null, "/processing?workflow=2");

    const link = within(panel).getByRole("link", {
      name: "Full detail: Health",
    });
    const { searchParams } = new URL(
      link.getAttribute("href") ?? "",
      "http://weir.test",
    );
    expect(searchParams.get("view")).toBe("system");
    expect(searchParams.get("workflow")).toBe("2");
  });
});
