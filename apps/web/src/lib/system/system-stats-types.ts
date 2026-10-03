/**
 * What GET /api/v1/system/stats and /system/overview answer, and what the `system.stats` stream frame carries, from the
 * server's OpenAPI document. A reading that cannot be taken (disk I/O on a network share, a container without access)
 * is null, never made up.
 */
import type { Schema } from "../api/types";

/** One second's readings of the machine and of Weir's own work. */
export type SystemNow = Schema<"SystemStatsNowOut">;
/** One sample of the ten-minute history, with its own time. */
export type SystemPoint = Schema<"SystemStatsPointOut">;
export type SystemDrive = Schema<"SystemStatsDriveOut">;
export type SystemStats = Schema<"SystemStatsOut">;
/** The `system.stats` frame: the newest reading and the one history point it adds. */
export type SystemStatsFrame = Schema<"SystemStatsFrame">;
export type SystemOverview = Schema<"SystemOverviewOut">;
export type RunsAs = SystemOverview["runs_as"];
