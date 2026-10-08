import { describe, expect, it } from "vitest";

import { downloadClientKeys } from "../download-clients/query-keys";
import { mediaManagerKeys } from "../media-managers/query-keys";
import { pauseKeys } from "../pause/query-keys";
import { processingKeys } from "../processing/query-keys";
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

  it("refreshes the connection lists when a connection changes", () => {
    expect(LIVE_TOPIC_QUERIES.connections).toEqual([
      mediaManagerKeys.connections,
      downloadClientKeys.connections,
    ]);
  });

  it("refreshes the workflows, their profiles and the libraries setup offers when a workflow changes", () => {
    expect(LIVE_TOPIC_QUERIES.libraries).toEqual([
      processingKeys.libraries,
      processingKeys.ruleSets,
      processingKeys.allLibrarySuggestions,
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
