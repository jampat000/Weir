import { describe, expect, it } from "vitest";

import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import type { ProcessingLibrary } from "../../../lib/processing/libraries-api";
import {
  NO_FREE_SPACE_LIMIT,
  connectionDetails,
  diskRows,
} from "./health-detail-model";

const NOW = Date.parse("2026-10-02T12:00:00Z");

const radarr = {
  id: 1,
  kind: "radarr",
  name: "Radarr on MEDIA-PC",
  nickname: "4K",
  enabled: true,
  base_url: "http://media-pc:7878",
  last_test_ok: true,
  last_test_at: new Date(NOW - 30_000).toISOString(),
  last_test_detail: "Connected. Radarr 5.9.",
} as MediaManagerConnection;

const qbittorrent = {
  id: 2,
  kind: "qbittorrent",
  name: "qBittorrent on NAS",
  enabled: false,
  base_url: "http://nas:8080",
  last_test_ok: false,
  last_test_detail: "Connection refused.",
} as DownloadClientConnection;

describe("the connections in full", () => {
  it("say what each is, where it is, when it last answered and what it said", () => {
    const [manager] = connectionDetails([radarr], [], NOW);

    expect(manager).toEqual({
      key: "manager-1",
      name: "Radarr on MEDIA-PC · 4K",
      role: "Media manager",
      kind: "Radarr",
      address: "http://media-pc:7878",
      state: "answered 30s ago",
      tone: "healthy",
      detail: "Connected. Radarr 5.9.",
    });
  });

  it("list a switched-off connection as switched off, whatever its last test said", () => {
    const [, client] = connectionDetails([radarr], [qbittorrent], NOW);

    expect(client).toMatchObject({
      key: "client-2",
      role: "Download client",
      kind: "qBittorrent",
      state: "switched off",
      tone: "neutral",
    });
  });
});

describe("the disk space each workflow keeps free", () => {
  const workflow = (megabytes: number) =>
    ({
      id: 4,
      name: "Movies",
      output_folder: "D:/clean/movies",
      minimum_free_disk_space_mb: megabytes,
    }) as ProcessingLibrary;

  it("reads the amount in the units a drive is measured in", () => {
    expect(diskRows([workflow(10_240)])).toEqual([
      {
        key: 4,
        workflow: "Movies",
        outputFolder: "D:/clean/movies",
        keepFree: "10.00 GB",
      },
    ]);
  });

  it("says there is no limit when the check is off", () => {
    expect(diskRows([workflow(0)])[0].keepFree).toBe(NO_FREE_SPACE_LIMIT);
  });
});
