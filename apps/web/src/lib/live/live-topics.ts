import type { QueryKey } from "@tanstack/react-query";

import type { Schema } from "../api/types";
import { downloadClientKeys } from "../download-clients/query-keys";
import { mediaManagerKeys } from "../media-managers/query-keys";
import { pauseKeys } from "../pause/query-keys";
import { processingKeys } from "../processing/query-keys";
import { settingsKeys } from "../settings/query-keys";
import { systemKeys } from "../system/query-keys";

/** The name of the frame on the Activity stream that says one kind of data changed. */
export const DATA_CHANGED_EVENT = "data.changed";

/** Each kind of data the server can say changed, as the server names it. */
export type LiveTopic = Schema<"DataChangedFrame">["topic"];

/**
 * The queries to read again when the server says a kind of data changed. Every topic is named here, so a topic the
 * server adds fails the build until it says what it refreshes. A screen that starts showing data a topic
 * covers adds its queries to that topic.
 */
export const LIVE_TOPIC_QUERIES: Readonly<
  Record<LiveTopic, readonly QueryKey[]>
> = {
  pause: [pauseKeys.state],
  readiness: [systemKeys.readiness],
  files_at_once: [processingKeys.filesAtOnce],
  maintenance: [processingKeys.maintenance],
  libraries: [processingKeys.libraries],
  library_scan: [],
  update: [
    settingsKeys.updateStatus,
    settingsKeys.updateSettings,
    settingsKeys.updateState,
  ],
  network_access: [settingsKeys.networkAccess],
  connections: [mediaManagerKeys.connections, downloadClientKeys.connections],
  settings: [
    settingsKeys.app,
    settingsKeys.notificationChannels,
    processingKeys.operatorSettings,
    processingKeys.runtimeSettings,
  ],
  backups: [settingsKeys.configurationBackups],
  kept_files: [processingKeys.keptFiles],
  metrics: [settingsKeys.metrics],
  jobs: [processingKeys.jobs, systemKeys.logEntriesAll],
};

function isLiveTopic(value: unknown): value is LiveTopic {
  return typeof value === "string" && Object.hasOwn(LIVE_TOPIC_QUERIES, value);
}

/** The topic in a `data.changed` frame, or null for one this version of the app does not know. */
export function parseDataChanged(data: string): LiveTopic | null {
  try {
    const parsed = JSON.parse(data) as { topic?: unknown };
    return isLiveTopic(parsed.topic) ? parsed.topic : null;
  } catch {
    return null;
  }
}
