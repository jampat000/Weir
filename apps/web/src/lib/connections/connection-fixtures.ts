import type { ConnectionEntry } from "./connection-model";

/** A media manager that is answering, for the tests of every screen that shows a connection. */
export function connectionEntry(
  overrides: Partial<ConnectionEntry> = {},
): ConnectionEntry {
  return {
    key: "media_manager:1",
    kind: "media_manager",
    id: 1,
    name: "Radarr on MEDIA-PC",
    baseName: "Radarr on MEDIA-PC",
    nickname: "",
    kindLabel: "Radarr",
    address: "http://localhost:7878",
    enabled: true,
    state: "ok",
    checkedAt: null,
    answerMs: null,
    detail: "",
    testable: true,
    ...overrides,
  };
}
