/** The stream frame that says Weir is talking to a media manager or download client, or that one is talking to Weir. */
import type { Schema } from "../api/types";

/** The name of the frame on the Activity stream. */
export const CONNECTION_ACTIVITY_EVENT = "connection.activity";

/**
 * What one `connection.activity` frame says. `asked` starts an outbound call; `answered` or `failed` ends it, with
 * how long it took. An inbound call, from the connection to Weir, is a single `answered`.
 */
export type ConnectionActivityFrame = Schema<"ConnectionActivityFrame">;
export type ConnectionKind = ConnectionActivityFrame["kind"];
export type ConnectionPhase = ConnectionActivityFrame["phase"];

const KINDS: readonly string[] = [
  "media_manager",
  "download_client",
] satisfies ConnectionKind[];
const PHASES: readonly string[] = [
  "asked",
  "answered",
  "failed",
] satisfies ConnectionPhase[];
const DIRECTIONS: readonly string[] = [
  "outbound",
  "inbound",
] satisfies ConnectionActivityFrame["direction"][];

/** The frame in a stream message, or null when it is not one this screen understands. */
export function parseConnectionActivity(
  data: string,
): ConnectionActivityFrame | null {
  try {
    const raw = JSON.parse(data) as Record<string, unknown>;
    if (
      typeof raw.kind !== "string" ||
      !KINDS.includes(raw.kind) ||
      typeof raw.id !== "number" ||
      typeof raw.phase !== "string" ||
      !PHASES.includes(raw.phase) ||
      typeof raw.at !== "string"
    )
      return null;
    return {
      kind: raw.kind as ConnectionKind,
      id: raw.id,
      phase: raw.phase as ConnectionPhase,
      direction:
        typeof raw.direction === "string" && DIRECTIONS.includes(raw.direction)
          ? (raw.direction as ConnectionActivityFrame["direction"])
          : "outbound",
      at: raw.at,
      ms: typeof raw.ms === "number" ? raw.ms : null,
    };
  } catch {
    return null;
  }
}
