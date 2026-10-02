import { act, fireEvent, render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { noteConnectionActivity } from "../../../lib/connections/connection-lights";
import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import type { CheckNow } from "./check-now";
import { HealthPanel } from "./health-panel";
import type { Health } from "./use-health";

const noRecheck = async () => undefined;

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

let fits = Number.MAX_SAFE_INTEGER;
vi.mock("./fit-rows", () => ({
  useFittingRows: () => [{ current: null }, fits],
}));
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
  fits = Number.MAX_SAFE_INTEGER;
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
        checkedAt: null,
        recheck: noRecheck,
      },
    ];
    const panel = renderPanel();

    const link = within(panel).getByRole("link", { name: /Movies/ });
    expect(link).toHaveAttribute("href", "/setup/workflows?edit=2");
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
        checkedAt: null,
        recheck: noRecheck,
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Needs a fix");
    expect(panel).toHaveTextContent("The output folder is missing.");
  });

  it("keeps the line to the first sentence of the problem, with the whole of it as a tooltip", () => {
    const whole =
      "Radarr does not say where Transmission saves its downloads. Weir cannot verify they land in D:/Downloads. Connect it under Settings.";
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "Not verified", tone: "neutral" },
        why: whole,
        chain: undefined,
        checkedAt: null,
        recheck: noRecheck,
      },
    ];
    const panel = renderPanel();

    const line = panel.querySelector(".mm-health__why");
    expect(line).toHaveTextContent(
      /^Radarr does not say where Transmission saves its downloads\.$/,
    );
    expect(line).toHaveAttribute("title", whole);
  });

  it("names the manager a linked workflow is linked to", () => {
    health.managers = [
      {
        id: 1,
        kind: "radarr",
        name: "Radarr on MEDIA-PC",
        enabled: true,
        base_url: "http://localhost:7878",
      },
    ] as Health["managers"];
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [1] },
        verdict: { words: "In sync", tone: "healthy" },
        why: null,
        chain: undefined,
        checkedAt: null,
        recheck: noRecheck,
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Linked to Radarr on MEDIA-PC");
  });

  it("says no workflow is on, rather than leaving the section empty", () => {
    const panel = renderPanel();

    expect(panel).toHaveTextContent("No workflow switched on.");
  });

  it("shows each switched-on connection as a row: its name, what it is, when it last answered and how long that took", () => {
    health.managers = [
      {
        id: 1,
        kind: "radarr",
        name: "Radarr",
        enabled: true,
        base_url: "http://localhost:7878",
        last_test_ok: true,
        last_test_at: secondsBefore(12),
        last_answer_ms: 84,
      },
      {
        id: 2,
        kind: "sonarr",
        name: "Sonarr",
        enabled: false,
        base_url: "http://localhost:8989",
      },
    ] as Health["managers"];
    health.downloadClients = [
      {
        id: 1,
        kind: "qbittorrent",
        name: "qBittorrent",
        enabled: true,
        base_url: "http://localhost:8080",
        last_test_ok: false,
      } as DownloadClientConnection,
    ];
    const panel = renderPanel();

    const rows = within(panel).getAllByTestId("live-connection");
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent("Radarr");
    expect(rows[0]).toHaveTextContent("manager");
    expect(rows[0]).toHaveTextContent("12s ago · 84 ms");
    expect(rows[1]).toHaveTextContent("qBittorrent");
    expect(rows[1]).toHaveTextContent("client");
    expect(rows[1]).toHaveTextContent("not answering");
    expect(panel).not.toHaveTextContent("Sonarr");
  });

  it("lights a connection's row while the stream says Weir is talking to it, and puts it out when the call ends", () => {
    health.managers = [
      {
        id: 7,
        kind: "radarr",
        name: "Radarr",
        enabled: true,
        base_url: "http://localhost:7878",
        last_test_ok: true,
        last_test_at: secondsBefore(5),
      },
    ] as Health["managers"];
    const panel = renderPanel();
    const row = () => within(panel).getByTestId("live-connection");
    const send = (phase: "asked" | "answered") =>
      act(() =>
        noteConnectionActivity({
          kind: "media_manager",
          id: 7,
          phase,
          direction: "outbound",
          at: new Date(NOW).toISOString(),
          ms: phase === "answered" ? 90 : null,
        }),
      );

    send("asked");
    expect(row()).toHaveAttribute("data-status", "doing");
    send("answered");

    expect(row()).toHaveAttribute("data-status", "done");
    expect(row()).toHaveTextContent("90 ms");
  });

  it("says nothing is connected rather than leaving the section empty", () => {
    expect(renderPanel()).toHaveTextContent("Nothing connected.");
  });

  it("shows only the rows that fit, and says how many more there are with a link to the full detail", () => {
    health.workflows = [movies, { ...movies, id: 3, name: "TV" }].map(
      (workflow) => ({
        workflow: { ...workflow, manager_connection_ids: [] },
        verdict: { words: "In sync", tone: "healthy" as const },
        why: null,
        chain: undefined,
        checkedAt: null,
        recheck: noRecheck,
      }),
    );
    health.managers = [1, 2, 3].map((id) => ({
      id,
      kind: "radarr",
      name: `Radarr ${id}`,
      enabled: true,
      base_url: "http://localhost:7878",
      last_test_ok: true,
    })) as Health["managers"];
    // Two headings, two workflows, three connections and the tools are eight units: five fit, so the last two rows do not.
    fits = 5;
    const panel = renderPanel();

    expect(panel.querySelectorAll("[data-fit]")).toHaveLength(8);
    const more = within(panel).getByRole("link", {
      name: "Full detail: Health",
    });
    expect(more).toHaveTextContent("2 more");
    const { searchParams } = new URL(
      more.getAttribute("href") ?? "",
      "http://weir.test",
    );
    expect(searchParams.get("view")).toBe("system");
  });

  it("says nothing about more when every row fits", () => {
    health.managers = [
      {
        id: 1,
        kind: "radarr",
        name: "Radarr",
        enabled: true,
        base_url: "http://localhost:7878",
      },
    ] as Health["managers"];

    expect(renderPanel()).not.toHaveTextContent("more");
  });

  it("counts what to look at beside its title, and says all clear when nothing is", () => {
    expect(renderPanel()).toHaveTextContent("all clear");
  });

  it("names the workflows Weir could not verify rather than saying all clear", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [1] },
        verdict: { words: "Not verified", tone: "neutral" },
        why: "Radarr does not say where Transmission saves its downloads.",
        chain: undefined,
        checkedAt: null,
        recheck: noRecheck,
      },
    ];

    expect(renderPanel()).toHaveTextContent("1 not verified");
  });

  it("counts a workflow that needs a fix and a connection that does not answer", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "Needs a fix", tone: "warning" },
        why: "It is missing.",
        chain: undefined,
        checkedAt: null,
        recheck: noRecheck,
      },
    ];
    health.downloadClients = [
      {
        id: 1,
        kind: "qbittorrent",
        name: "qBittorrent",
        enabled: true,
        base_url: "http://localhost:8080",
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
