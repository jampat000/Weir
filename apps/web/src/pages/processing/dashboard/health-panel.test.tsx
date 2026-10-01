import { render, screen, within } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";

import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import { HealthPanel } from "./health-panel";
import type { Health } from "./use-health";

const health: Health = {
  workflows: [],
  managers: [],
  connections: [],
  tools: null,
};

vi.mock("./use-health", () => ({ useHealth: () => health }));

const movies = { id: 2, name: "Movies" } as ProcessingLibrary;

function renderPanel() {
  render(
    <MemoryRouter>
      <HealthPanel workflows={[movies]} />
    </MemoryRouter>,
  );
  return screen.getByRole("region", { name: "Health" });
}

beforeEach(() => {
  health.workflows = [];
  health.managers = [];
  health.connections = [];
  health.tools = null;
});

describe("the Health panel", () => {
  it("lists each workflow with how it is linked and its folder-chain verdict, linked to the workflow", () => {
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [] },
        verdict: { words: "Needs a fix", tone: "warning" },
      },
    ];
    const panel = renderPanel();

    const link = within(panel).getByRole("link", { name: /Movies/ });
    expect(link).toHaveAttribute("href", "/settings?tab=libraries&edit=2");
    expect(link).toHaveTextContent("Weir only");
    expect(panel).toHaveTextContent("Needs a fix");
  });

  it("names the manager a linked workflow is linked to", () => {
    health.managers = [
      { id: 1, kind: "radarr", name: "Radarr on MEDIA-PC" },
    ] as Health["managers"];
    health.workflows = [
      {
        workflow: { ...movies, manager_connection_ids: [1] },
        verdict: { words: "In sync", tone: "healthy" },
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Linked to Radarr on MEDIA-PC");
  });

  it("says no workflow is on, rather than leaving the section empty", () => {
    const panel = renderPanel();

    expect(panel).toHaveTextContent("No workflow is switched on.");
  });

  it("shows each connection with whether it answers, in words", () => {
    health.connections = [
      { key: "manager-1", name: "Radarr", state: "answering", tone: "healthy" },
      {
        key: "client-1",
        name: "qBittorrent",
        state: "not answering",
        tone: "failed",
      },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("Radarr answering");
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
      },
    ];
    health.connections = [
      {
        key: "client-1",
        name: "qBittorrent",
        state: "not answering",
        tone: "failed",
      },
    ];

    expect(renderPanel()).toHaveTextContent("2 to look at");
  });

  it("shows the tools' versions, or says it is reading them", () => {
    expect(renderPanel()).toHaveTextContent("Reading the tools…");
  });

  it("lists FFmpeg and mkvmerge with their versions", () => {
    health.tools = [
      { key: "ffmpeg", name: "FFmpeg", version: "7.1.1", tone: "healthy" },
      { key: "mkvmerge", name: "mkvmerge", version: "89.0.0", tone: "healthy" },
    ];
    const panel = renderPanel();

    expect(panel).toHaveTextContent("FFmpeg 7.1.1");
    expect(panel).toHaveTextContent("mkvmerge 89.0.0");
  });
});
