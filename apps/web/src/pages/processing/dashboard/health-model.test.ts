import { describe, expect, it } from "vitest";

import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import type {
  FolderChainLine,
  LibraryFolderChain,
} from "../../../lib/processing/library-folder-chain-api";
import {
  chainVerdict,
  connectionPills,
  problemCount,
  toolRows,
} from "./health-model";

function chain(
  ready: boolean,
  lines: FolderChainLine[] = [],
  clientLines: FolderChainLine[] = [],
): LibraryFolderChain {
  return {
    library_id: 1,
    ready,
    local: { ready, lines },
    managers: [],
    download_clients: clientLines.length
      ? [
          {
            connection_id: 1,
            kind: "qbittorrent",
            name: "qBittorrent",
            label: "qBittorrent",
            ready,
            lines: clientLines,
          },
        ]
      : [],
  };
}

const ok: FolderChainLine = { state: "ok", text: "Weir can read it." };
const unverified: FolderChainLine = {
  state: "unverified",
  text: "Weir cannot see this folder from here.",
};
const problem: FolderChainLine = { state: "problem", text: "It is missing." };

describe("a workflow's folder-chain verdict", () => {
  it("is In sync when every line was read by Weir and none is a problem", () => {
    expect(chainVerdict(chain(true, [ok]))).toEqual({
      words: "In sync",
      tone: "healthy",
    });
  });

  it("is Not verified while any line rests on someone's word, in a manager's or a client's lines too", () => {
    expect(chainVerdict(chain(true, [ok], [unverified])).words).toBe(
      "Not verified",
    );
  });

  it("is Needs a fix when the chain is not ready", () => {
    expect(chainVerdict(chain(false, [problem]))).toEqual({
      words: "Needs a fix",
      tone: "warning",
    });
  });
});

function manager(
  overrides: Partial<MediaManagerConnection>,
): MediaManagerConnection {
  return {
    id: 1,
    kind: "radarr",
    name: "Radarr on MEDIA-PC",
    enabled: true,
    last_test_ok: true,
    ...overrides,
  } as MediaManagerConnection;
}

function client(
  overrides: Partial<DownloadClientConnection>,
): DownloadClientConnection {
  return {
    id: 1,
    kind: "qbittorrent",
    name: "qBittorrent on MEDIA-PC",
    enabled: true,
    last_test_ok: true,
    ...overrides,
  } as DownloadClientConnection;
}

describe("the connection pills", () => {
  it("say in words whether each connection answered its last test", () => {
    const pills = connectionPills(
      [
        manager({ id: 1 }),
        manager({ id: 2, name: "Sonarr", last_test_ok: false }),
      ],
      [client({ last_test_ok: null })],
    );

    expect(pills.map((pill) => [pill.name, pill.state, pill.tone])).toEqual([
      ["Radarr on MEDIA-PC", "answering", "healthy"],
      ["Sonarr", "not answering", "failed"],
      ["qBittorrent on MEDIA-PC", "not tested yet", "neutral"],
    ]);
  });

  it("leave out a connection that is switched off", () => {
    expect(
      connectionPills(
        [manager({ enabled: false })],
        [client({ enabled: false })],
      ),
    ).toEqual([]);
  });

  it("carry a nickname after the name", () => {
    const [pill] = connectionPills([manager({ nickname: "4K" })], []);

    expect(pill.name).toBe("Radarr on MEDIA-PC · 4K");
  });
});

describe("the tools", () => {
  it("show each version in a few words", () => {
    expect(
      toolRows({
        ffmpeg: "ffmpeg version 7.1.1 Copyright",
        mkvmerge: "v89.0.0",
      }),
    ).toEqual([
      { key: "ffmpeg", name: "FFmpeg", version: "7.1.1", tone: "healthy" },
      { key: "mkvmerge", name: "mkvmerge", version: "89.0.0", tone: "healthy" },
    ]);
  });

  it("call a missing FFmpeg a failure, and a missing mkvmerge only a note", () => {
    const [ffmpeg, mkvmerge] = toolRows({
      ffmpeg: "not installed",
      mkvmerge: "not installed",
    });

    expect(ffmpeg.tone).toBe("failed");
    expect(mkvmerge.tone).toBe("neutral");
  });
});

describe("how many things need a look", () => {
  it("counts a workflow that needs a fix, a connection that is down and a missing FFmpeg", () => {
    expect(
      problemCount({
        workflows: [
          { verdict: { words: "Needs a fix", tone: "warning" } },
          { verdict: { words: "In sync", tone: "healthy" } },
        ],
        connections: [
          { key: "a", name: "Radarr", state: "not answering", tone: "failed" },
          {
            key: "b",
            name: "Sonarr",
            state: "not tested yet",
            tone: "neutral",
          },
        ],
        tools: toolRows({ ffmpeg: "not installed", mkvmerge: "not installed" }),
      }),
    ).toBe(3);
  });

  it("is nothing when all is well, or while the tools are still being read", () => {
    expect(problemCount({ workflows: [], connections: [], tools: null })).toBe(
      0,
    );
  });
});
