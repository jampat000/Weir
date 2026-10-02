import { describe, expect, it } from "vitest";

import type { DownloadClientConnection } from "../download-clients/download-clients-api";
import type { MediaManagerConnection } from "../media-managers/media-managers-api";
import {
  SLOW_ANSWER_MS,
  answerWords,
  connectionEntries,
  connectionKey,
  type ConnectionAnswer,
} from "./connection-model";

const NOW = Date.parse("2026-10-02T12:00:00Z");
const secondsBefore = (seconds: number) =>
  new Date(NOW - seconds * 1000).toISOString();

const radarr = (overrides: Partial<MediaManagerConnection> = {}) =>
  ({
    id: 1,
    kind: "radarr",
    name: "Radarr on MEDIA-PC",
    enabled: true,
    base_url: "http://localhost:7878",
    last_test_ok: true,
    last_test_at: secondsBefore(30),
    last_test_detail: "Connected.",
    last_answer_ms: 84,
    last_used_at: secondsBefore(30),
    ...overrides,
  }) as MediaManagerConnection;

const qbittorrent = (overrides: Partial<DownloadClientConnection> = {}) =>
  ({
    id: 2,
    kind: "qbittorrent",
    name: "qBittorrent on NAS",
    enabled: true,
    base_url: "http://nas:8080",
    last_test_ok: true,
    last_test_at: secondsBefore(30),
    ...overrides,
  }) as DownloadClientConnection;

const NO_ANSWERS = new Map<string, ConnectionAnswer>();

function entryOf(
  connection: MediaManagerConnection,
  answers: ReadonlyMap<string, ConnectionAnswer> = NO_ANSWERS,
) {
  return connectionEntries([connection], [], answers)[0];
}

describe("a connection's state", () => {
  it("is ok when its last test passed and was quick", () => {
    expect(entryOf(radarr()).state).toBe("ok");
  });

  it("is slow when its last call took two seconds or more", () => {
    expect(entryOf(radarr({ last_answer_ms: SLOW_ANSWER_MS })).state).toBe(
      "slow",
    );
    expect(entryOf(radarr({ last_answer_ms: SLOW_ANSWER_MS - 1 })).state).toBe(
      "ok",
    );
  });

  it("is down when its last test did not pass", () => {
    expect(entryOf(radarr({ last_test_ok: false })).state).toBe("down");
  });

  it("is untested when it has never been tested", () => {
    expect(entryOf(radarr({ last_test_ok: null })).state).toBe("untested");
  });

  it("is off when it is switched off, whatever its last test said", () => {
    expect(entryOf(radarr({ enabled: false, last_test_ok: false })).state).toBe(
      "off",
    );
  });
});

describe("what a connection entry says", () => {
  it("names what it is, where Weir reaches it and what its last test said", () => {
    expect(entryOf(radarr({ nickname: "4K" }))).toMatchObject({
      key: "media_manager:1",
      name: "Radarr on MEDIA-PC · 4K",
      kindLabel: "Radarr",
      address: "http://localhost:7878",
      detail: "Connected.",
      answerMs: 84,
      testable: true,
    });
  });

  it("takes the later of its last test and its last use as when it was checked", () => {
    const entry = entryOf(radarr({ last_used_at: secondsBefore(5) }));

    expect(entry.checkedAt).toBe(NOW - 5000);
  });

  it("cannot be tested when it is switched off or has no address", () => {
    expect(entryOf(radarr({ enabled: false })).testable).toBe(false);
    expect(entryOf(radarr({ base_url: " " })).testable).toBe(false);
  });

  it("lists the media managers, then the download clients", () => {
    const entries = connectionEntries([radarr()], [qbittorrent()], NO_ANSWERS);

    expect(entries.map((entry) => entry.key)).toEqual([
      "media_manager:1",
      "download_client:2",
    ]);
    expect(entries[1].kindLabel).toBe("qBittorrent");
  });

  it("copes with a server that does not say how long a call took", () => {
    const entry = entryOf(
      radarr({ last_answer_ms: undefined, last_used_at: undefined }),
    );

    expect(entry.answerMs).toBeNull();
    expect(entry.checkedAt).toBe(NOW - 30_000);
  });
});

describe("an answer pushed on the stream", () => {
  const key = connectionKey("media_manager", 1);
  const answer = (overrides: Partial<ConnectionAnswer>) =>
    new Map([[key, { at: NOW, ms: 2500, ok: true, ...overrides }]]);

  it("brings a connection up to date when it is newer than what was saved", () => {
    const entry = entryOf(radarr(), answer({}));

    expect(entry).toMatchObject({
      state: "slow",
      answerMs: 2500,
      checkedAt: NOW,
    });
  });

  it("is passed over when the saved connection is newer", () => {
    const entry = entryOf(radarr(), answer({ at: NOW - 60_000 }));

    expect(entry).toMatchObject({ state: "ok", answerMs: 84 });
  });

  it("turns a connection down when Weir's call to it failed", () => {
    expect(entryOf(radarr(), answer({ ok: false })).state).toBe("down");
  });

  it("only moves the time when the connection called Weir, and says nothing about the answer", () => {
    const entry = entryOf(radarr(), answer({ ok: null, ms: null }));

    expect(entry).toMatchObject({
      state: "ok",
      answerMs: 84,
      checkedAt: NOW,
    });
  });
});

describe("how long an answer took", () => {
  it("is said in milliseconds", () => {
    expect(answerWords(84)).toBe("84 ms");
    expect(answerWords(2384.4)).toBe("2,384 ms");
  });
});
