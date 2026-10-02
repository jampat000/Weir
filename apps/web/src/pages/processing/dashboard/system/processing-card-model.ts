/** What the Processing card says: Weir's own disk work and speed over the last ten minutes, and what it finished. */
import type { TraceSample } from "../../../../components/charts/live-trace-math";
import { seriesOf } from "../../../../lib/system/system-stats-model";
import type { SystemStats } from "../../../../lib/system/system-stats-types";
import { plural } from "../../../../lib/ui/mm-plural";
import type { Words } from "./fit-words";
import { megabytes, rateFigure, sizeWords } from "./system-words";

/** The window the card's finished-work figures cover, in minutes: the same ten minutes the traces show. */
export const RECENT_MINUTES = 10;

/** A disk trace is never scaled tighter than this, in MB/s, so quiet work reads as quiet. */
const DISK_FLOOR_MB = 1;
/** A speed trace is never scaled tighter than this, in times real time. */
const SPEED_FLOOR = 10;
/** A speed of this or more reads as a whole number. */
const WHOLE_SPEED_FROM = 10;

export type TraceLine = {
  key: string;
  label: string;
  colour: string;
  samples: TraceSample[];
};

export type ProcessingColumn = {
  key: "disk" | "speed";
  label: string;
  colour: string;
  /** The big figure, or null when there is none to show. */
  value: number | null;
  /** Writes the figure as it counts. */
  figure: (value: number) => string;
  unit: string;
  /** The line of detail, in words that narrow with the room, the fullest first. */
  sub: Words;
  lines: TraceLine[];
  floorTop: number;
  /** Writes a value of the trace for its readout. */
  readout: (value: number) => string;
};

/** A speed as a person reads it: "0.8", "35", "1,260". */
export function speedFigure(times: number): string {
  if (times >= WHOLE_SPEED_FROM)
    return Math.round(times).toLocaleString("en-US");
  return times.toFixed(1).replace(/\.0$/, "");
}

/** The card's two columns: what the running passes read and write, and how fast they go. */
export function processingColumns(stats: SystemStats): ProcessingColumn[] {
  const { now, history } = stats;
  return [
    {
      key: "disk",
      label: "Disk work",
      colour: "var(--mm-lane-processing)",
      value: megabytes(now.processing_write_bytes_per_sec),
      figure: rateFigure,
      unit: " MB/s",
      sub: (() => {
        const read = rateFigure(megabytes(now.processing_read_bytes_per_sec));
        return [`writing · reading ${read}`, `reading ${read}`, `read ${read}`];
      })(),
      lines: [
        {
          key: "read",
          label: "read",
          colour: "var(--mm-lane-queued)",
          samples: seriesOf(history, (point) =>
            megabytes(point.processing_read_bytes_per_sec),
          ),
        },
        {
          key: "write",
          label: "write",
          colour: "var(--mm-lane-processing)",
          samples: seriesOf(history, (point) =>
            megabytes(point.processing_write_bytes_per_sec),
          ),
        },
      ],
      floorTop: DISK_FLOOR_MB,
      readout: (value) => `${rateFigure(value)} MB/s`,
    },
    {
      key: "speed",
      label: "Speed",
      colour: "var(--mm-lane-delivering)",
      value: now.running === 0 ? null : now.processing_speed,
      figure: speedFigure,
      unit: "×",
      sub:
        now.running === 0
          ? ["nothing running", "idle"]
          : [
              `across ${plural(now.running, "file", "files")}`,
              plural(now.running, "file", "files"),
            ],
      lines: [
        {
          key: "speed",
          label: "speed",
          colour: "var(--mm-lane-delivering)",
          samples: seriesOf(history, (point) => point.processing_speed),
        },
      ],
      floorTop: SPEED_FLOOR,
      readout: (value) => `${speedFigure(value)}×`,
    },
  ];
}

export type RecentWork = {
  /** Files Weir finished in the last ten minutes. */
  done: number;
  /** The space those saved, in bytes. */
  savedBytes: number;
};

export type WorkPill = { key: string; text: string };

/** The pills under the columns: passes running over slots, and what the last ten minutes came to. */
export function workPills(
  now: SystemStats["now"],
  recent: RecentWork | undefined,
): WorkPill[] {
  const pills: WorkPill[] = [
    { key: "running", text: `${now.running} of ${now.slots} running` },
  ];
  if (recent) {
    pills.push({
      key: "done",
      text: `${plural(recent.done, "file", "files")} done`,
    });
    pills.push({ key: "saved", text: `${sizeWords(recent.savedBytes)} saved` });
  }
  return pills;
}
