import { describe, expect, it } from "vitest";

import type { MediaManagerConnection } from "../media-managers/media-managers-api";
import {
  workflowBadgeLabel,
  workflowKindCounts,
  workflowKindName,
  workflowKindNote,
  workflowKindOf,
} from "./workflow-kind";

function connection(
  id: number,
  kind: MediaManagerConnection["kind"],
  name: string,
): MediaManagerConnection {
  return { id, kind, name } as MediaManagerConnection;
}

const CONNECTIONS = [
  connection(1, "deluno", "Deluno"),
  connection(2, "sonarr", "Sonarr"),
  connection(3, "radarr", "Radarr"),
  connection(4, "native", "Home script"),
];

describe("a workflow's kind", () => {
  it("is named by the same rule everywhere: linked only when a media manager is linked", () => {
    expect(workflowKindName({ manager_connection_ids: [] })).toBe("weir_only");
    expect(workflowKindName({ manager_connection_ids: [1] })).toBe("linked");
    expect(workflowKindName({ manager_connection_ids: [99] })).toBe("linked");
  });

  it("is Weir only when no media manager is linked", () => {
    const kind = workflowKindOf({ manager_connection_ids: [] }, CONNECTIONS);

    expect(kind).toEqual({ kind: "weir_only" });
    expect(workflowBadgeLabel(kind)).toBe("Weir only");
    expect(workflowKindNote(kind)).toBe(
      "Files that appear in the watched folder are cleaned into the output folder. Nothing else is involved.",
    );
  });

  it("names the media manager it is linked to", () => {
    const kind = workflowKindOf({ manager_connection_ids: [1] }, CONNECTIONS);

    expect(workflowBadgeLabel(kind)).toBe("Linked to Deluno");
    expect(workflowKindNote(kind)).toBe(
      "Deluno hands each finished download to Weir, then imports the cleaned file.",
    );
  });

  it("names a linked media manager with its nickname, so two of one kind can be told apart", () => {
    const nicknamed = [
      { ...connection(5, "radarr", "Radarr on nas"), nickname: "4K" },
    ];
    const kind = workflowKindOf({ manager_connection_ids: [5] }, nicknamed);

    expect(workflowBadgeLabel(kind)).toBe("Linked to Radarr on nas · 4K");
  });

  it("says how Sonarr and Radarr differ from Deluno", () => {
    const sonarr = workflowKindOf({ manager_connection_ids: [2] }, CONNECTIONS);

    expect(workflowKindNote(sonarr)).toBe(
      "Weir watches the folder Sonarr downloads to, and Sonarr imports the cleaned files.",
    );
  });

  it("joins several links in plain words", () => {
    const kind = workflowKindOf(
      { manager_connection_ids: [1, 3, 2] },
      CONNECTIONS,
    );

    expect(workflowBadgeLabel(kind)).toBe(
      "Linked to Deluno, Radarr and Sonarr",
    );
  });

  it("stays linked when the media manager it points at is gone", () => {
    const kind = workflowKindOf({ manager_connection_ids: [99] }, CONNECTIONS);

    expect(workflowBadgeLabel(kind)).toBe("Linked to a removed media manager");
  });

  it("describes a manager of another kind by what it does for the workflow", () => {
    const kind = workflowKindOf({ manager_connection_ids: [4] }, CONNECTIONS);

    expect(workflowKindNote(kind)).toBe(
      "Home script sends files to Weir and hears back when they are cleaned.",
    );
  });
});

describe("counting workflows by kind", () => {
  it("counts each kind that exists", () => {
    const kinds = [
      workflowKindOf({ manager_connection_ids: [1] }, CONNECTIONS),
      workflowKindOf({ manager_connection_ids: [] }, CONNECTIONS),
      workflowKindOf({ manager_connection_ids: [2] }, CONNECTIONS),
    ];

    expect(workflowKindCounts(kinds)).toBe(
      "2 linked to a media manager · 1 Weir only",
    );
  });

  it("leaves out a kind nothing is", () => {
    const kinds = [workflowKindOf({ manager_connection_ids: [] }, CONNECTIONS)];

    expect(workflowKindCounts(kinds)).toBe("1 Weir only");
  });
});
