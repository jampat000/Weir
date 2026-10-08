import { describe, expect, it } from "vitest";

import { connectionEntry } from "../../../lib/connections/connection-fixtures";
import type {
  FolderChainLine,
  LibraryFolderChain,
} from "../../../lib/processing/library-folder-chain-api";
import {
  chainVerdict,
  checkedAgo,
  newestTime,
  problemCount,
  rowsLeftOut,
  toolRows,
  whyNotInSync,
} from "./health-model";

const NOW = Date.parse("2026-10-02T12:00:00Z");
const secondsBefore = (seconds: number) => NOW - seconds * 1000;

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
      "Open it to see what to fix.",
    );
  });
});

describe("a workflow's folder-chain verdict", () => {
  it("is In sync when every line was read by Weir and none is a problem", () => {
    expect(chainVerdict(chain(true, [ok]))).toEqual({
      words: "In sync",
      meaning: "done",
      readiness: "ready",
    });
  });

  it("is Not verified, and needs attention, while any line rests on someone's word, in a manager's or a client's lines too", () => {
    expect(chainVerdict(chain(true, [ok], [unverified]))).toMatchObject({
      words: "Not verified",
      meaning: "attention",
    });
  });

  it("is Needs a fix when the chain is not ready", () => {
    expect(chainVerdict(chain(false, [problem]))).toEqual({
      words: "Needs a fix",
      meaning: "attention",
      readiness: "needs_attention",
    });
  });
});

describe("newestTime", () => {
  it("is the latest time known, ignoring the ones that are not", () => {
    expect(newestTime(5, null, 9, 7)).toBe(9);
  });

  it("is null when no time is known", () => {
    expect(newestTime(null, null)).toBeNull();
    expect(newestTime()).toBeNull();
  });
});

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
        meaning: "done",
      },
      {
        key: "mkvmerge",
        name: "mkvmerge",
        version: "89.0.0",
        banner: "v89.0.0",
        meaning: "done",
      },
    ]);
  });

  it("call a missing FFmpeg broken, and a missing mkvmerge idle", () => {
    const [ffmpeg, mkvmerge] = toolRows({
      ffmpeg: "not installed",
      mkvmerge: "not installed",
    });

    expect(ffmpeg.meaning).toBe("broken");
    expect(mkvmerge.meaning).toBe("idle");
  });
});

describe("how many things need a look", () => {
  it("counts a workflow that needs a fix, a connection that is down and a missing FFmpeg", () => {
    expect(
      problemCount({
        workflows: [
          {
            verdict: {
              words: "Needs a fix",
              meaning: "attention",
              readiness: "needs_attention",
            },
          },
          {
            verdict: { words: "In sync", meaning: "done", readiness: "ready" },
          },
        ],
        connections: [
          connectionEntry({ state: "down" }),
          connectionEntry({ key: "media_manager:2", state: "untested" }),
        ],
        tools: toolRows({ ffmpeg: "not installed", mkvmerge: "not installed" }),
      }),
    ).toBe(3);
  });

  it("counts a connection that is slow, and leaves out one that is switched off", () => {
    expect(
      problemCount({
        workflows: [],
        connections: [
          connectionEntry({ state: "slow" }),
          connectionEntry({
            key: "media_manager:2",
            enabled: false,
            state: "down",
          }),
        ],
        tools: null,
      }),
    ).toBe(1);
  });

  it("is nothing when all is well, or while the tools are still being read", () => {
    expect(problemCount({ workflows: [], connections: [], tools: null })).toBe(
      0,
    );
  });
});

describe("how many rows the panel leaves out", () => {
  const units = [
    "heading",
    "row",
    "row",
    "heading",
    "row",
    "row",
    "tools",
  ] as const;

  it("counts the rows after the ones that fit, never the headings or the tools", () => {
    expect(rowsLeftOut(units, 5)).toBe(1);
    expect(rowsLeftOut(units, 1)).toBe(4);
  });

  it("is nothing when everything fits", () => {
    expect(rowsLeftOut(units, Number.MAX_SAFE_INTEGER)).toBe(0);
  });
});
