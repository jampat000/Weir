import { describe, expect, it } from "vitest";

import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import type {
  FolderChainLine,
  LibraryFolderChain,
} from "../../../lib/processing/library-folder-chain-api";
import {
  chainVerdict,
  checkedAgo,
  connectionPills,
  problemCount,
  toolRows,
  whyNotInSync,
} from "./health-model";

const NOW = Date.parse("2026-10-02T12:00:00Z");
const secondsBefore = (seconds: number) =>
  new Date(NOW - seconds * 1000).toISOString();

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

describe("why a workflow is not in sync", () => {
  it("is nothing while the chain is in sync", () => {
    expect(whyNotInSync(chain(true, [ok]))).toBeNull();
  });

  it("names the first problem, before anything Weir could only take on trust", () => {
    expect(whyNotInSync(chain(false, [unverified, problem]))).toBe(
      "It is missing.",
    );
  });

  it("names the first unverified line when nothing is a problem, in a client's lines too", () => {
    expect(whyNotInSync(chain(true, [ok], [unverified]))).toBe(
      "Weir cannot see this folder from here.",
    );
  });

  it("sends the person to the workflow when the chain is not ready but names nothing", () => {
    expect(whyNotInSync(chain(false, [ok]))).toBe(
      "Open this workflow to see what needs a fix.",
    );
  });
});

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

describe("how long ago a check was", () => {
  it("counts seconds for the first minute, then minutes", () => {
    expect(checkedAgo(secondsBefore(12), NOW)).toBe("12s ago");
    expect(checkedAgo(secondsBefore(5 * 60), NOW)).toBe("5 min ago");
  });

  it("says just now for a check that has only just answered", () => {
    expect(checkedAgo(secondsBefore(1), NOW)).toBe("just now");
  });

  it("is empty when there was no check", () => {
    expect(checkedAgo(null, NOW)).toBe("");
  });
});

describe("the connection pills", () => {
  it("say in words whether each connection answered its last test, and how long ago", () => {
    const pills = connectionPills(
      [
        manager({ id: 1, last_test_at: secondsBefore(12) }),
        manager({ id: 2, name: "Sonarr", last_test_ok: false }),
      ],
      [client({ last_test_ok: null })],
      NOW,
    );

    expect(pills.map((pill) => [pill.name, pill.state, pill.tone])).toEqual([
      ["Radarr on MEDIA-PC", "answered 12s ago", "healthy"],
      ["Sonarr", "not answering", "failed"],
      ["qBittorrent on MEDIA-PC", "not tested yet", "neutral"],
    ]);
  });

  it("say answering when a test passed but was never timed", () => {
    const [pill] = connectionPills([manager({})], [], NOW);

    expect(pill.state).toBe("answering");
  });

  it("leave out a connection that is switched off", () => {
    expect(
      connectionPills(
        [manager({ enabled: false })],
        [client({ enabled: false })],
        NOW,
      ),
    ).toEqual([]);
  });

  it("carry a nickname after the name", () => {
    const [pill] = connectionPills([manager({ nickname: "4K" })], [], NOW);

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
      {
        key: "ffmpeg",
        name: "FFmpeg",
        version: "7.1.1",
        banner: "ffmpeg version 7.1.1 Copyright",
        tone: "healthy",
      },
      {
        key: "mkvmerge",
        name: "mkvmerge",
        version: "89.0.0",
        banner: "v89.0.0",
        tone: "healthy",
      },
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
