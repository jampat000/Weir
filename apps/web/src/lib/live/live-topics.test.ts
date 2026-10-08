import { describe, expect, it } from "vitest";

import { pauseKeys } from "../pause/query-keys";
import { processingKeys } from "../processing/query-keys";
import { settingsKeys } from "../settings/query-keys";
import { systemKeys } from "../system/query-keys";
import {
  LIVE_TOPIC_QUERIES,
  parseDataChanged,
  type LiveTopic,
} from "./live-topics";

describe("LIVE_TOPIC_QUERIES", () => {
  it("names every topic the server can send", () => {
    const topics: LiveTopic[] = [
      "backups",
      "connections",
      "files_at_once",
      "jobs",
      "kept_files",
      "libraries",
      "library_scan",
      "maintenance",
      "metrics",
      "network_access",
      "pause",
      "readiness",
      "settings",
      "update",
    ];

    expect(Object.keys(LIVE_TOPIC_QUERIES).sort()).toEqual(topics);
  });

  it("refreshes the pause queries when the pause changes", () => {
    expect(LIVE_TOPIC_QUERIES.pause).toEqual([pauseKeys.state]);
  });

  it("refreshes the jobs and the System log when a job changes", () => {
    expect(LIVE_TOPIC_QUERIES.jobs).toEqual([
      processingKeys.jobs,
      systemKeys.logEntriesAll,
    ]);
  });

  it("refreshes the server diagnostics when the counters move", () => {
    expect(LIVE_TOPIC_QUERIES.metrics).toEqual([settingsKeys.metrics]);
  });

  it("refreshes the update notice and the tray's network answer when the server says they changed", () => {
    expect(LIVE_TOPIC_QUERIES.update).toEqual([
      settingsKeys.updateStatus,
      settingsKeys.updateSettings,
      settingsKeys.updateState,
    ]);
    expect(LIVE_TOPIC_QUERIES.network_access).toEqual([
      settingsKeys.networkAccess,
    ]);
  });
});

describe("parseDataChanged", () => {
  it("reads the topic of a data.changed frame", () => {
    expect(parseDataChanged('{"topic":"pause"}')).toBe("pause");
    expect(parseDataChanged('{"topic":"files_at_once"}')).toBe("files_at_once");
  });

  it("ignores a topic this app does not know, and anything that is not a frame", () => {
    expect(parseDataChanged('{"topic":"from_a_newer_server"}')).toBeNull();
    expect(parseDataChanged('{"topic":"toString"}')).toBeNull();
    expect(parseDataChanged('{"topic":4}')).toBeNull();
    expect(parseDataChanged("{}")).toBeNull();
    expect(parseDataChanged("not json")).toBeNull();
  });
});
