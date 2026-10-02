/** What the This computer card says: three readings with their traces, and the machine's own tags. */
import type { TraceSample } from "../../../../components/charts/live-trace-math";
import { formatBytes } from "../../../../lib/format/bytes";
import { seriesOf } from "../../../../lib/system/system-stats-model";
import type {
  SystemNow,
  SystemStats,
} from "../../../../lib/system/system-stats-types";
import {
  gigabytes,
  machineUpWords,
  megabytes,
  rateFigure,
} from "./system-words";

export type ComputerKey = "cpu" | "memory" | "disk";

/** The scale a column's trace is drawn on: a fixed top for a percentage, or a rate that finds its own. */
export type ComputerScale =
  { kind: "percent" } | { kind: "rate"; floorTop: number };

export type ComputerColumn = {
  key: ComputerKey;
  label: string;
  /** The trace's colour, a design token. */
  colour: string;
  /** The big figure, or null when the machine cannot read it. */
  value: number | null;
  /** Writes the figure as it counts. */
  figure: (value: number) => string;
  unit: string;
  sub: string;
  samples: TraceSample[];
  scale: ComputerScale;
  /** Writes a value of the trace for its readout. */
  readout: (value: number) => string;
};

const wholeFigure = (value: number) => Math.round(value).toString();
const tenthsFigure = (value: number) => value.toFixed(1);
const percentReadout = (value: number) => `${Math.round(value)}%`;
const rateReadout = (value: number) => `${rateFigure(value)} MB/s`;

/** A disk trace is never scaled tighter than this, in MB/s, so an idle disk reads as idle. */
const DISK_FLOOR_MB = 1;

/** Reads and writes together, in MB/s; null only when neither can be read. */
export function diskMegabytes(
  read: number | null,
  write: number | null,
): number | null {
  if (read === null && write === null) return null;
  return megabytes((read ?? 0) + (write ?? 0));
}

const joined = (parts: readonly (string | null)[]) =>
  parts.filter(Boolean).join(" · ");

function cpuSub(now: SystemNow): string {
  return joined([
    `${now.cores} cores`,
    now.weir_cpu_percent === null
      ? null
      : `Weir ${Math.round(now.weir_cpu_percent)}%`,
    now.tools_cpu_percent === null
      ? null
      : `ffmpeg ${Math.round(now.tools_cpu_percent)}%`,
  ]);
}

function memorySub(now: SystemNow): string {
  return joined([
    now.memory_total_bytes === null
      ? null
      : `of ${gigabytes(now.memory_total_bytes).toFixed(1)} GB`,
    `Weir ${formatBytes(now.weir_memory_bytes)}`,
  ]);
}

function diskSub(now: SystemNow): string {
  if (
    now.disk_read_bytes_per_sec === null &&
    now.disk_write_bytes_per_sec === null
  ) {
    return "not readable here";
  }
  return joined([
    `read ${rateFigure(megabytes(now.disk_read_bytes_per_sec ?? 0))}`,
    `write ${rateFigure(megabytes(now.disk_write_bytes_per_sec ?? 0))}`,
    now.disk_busy_percent === null
      ? null
      : `${Math.round(now.disk_busy_percent)}% busy`,
  ]);
}

/** The three columns, CPU, memory and disk, each with its reading now and its last ten minutes. */
export function computerColumns(stats: SystemStats): ComputerColumn[] {
  const { now, history } = stats;
  return [
    {
      key: "cpu",
      label: "CPU",
      colour: "var(--mm-success)",
      value: now.cpu_percent,
      figure: wholeFigure,
      unit: "%",
      sub: cpuSub(now),
      samples: seriesOf(history, (point) => point.cpu_percent),
      scale: { kind: "percent" },
      readout: percentReadout,
    },
    {
      key: "memory",
      label: "Memory",
      colour: "var(--mm-info)",
      value:
        now.memory_used_bytes === null
          ? null
          : gigabytes(now.memory_used_bytes),
      figure: tenthsFigure,
      unit: " GB",
      sub: memorySub(now),
      samples: seriesOf(history, (point) => point.memory_percent),
      scale: { kind: "percent" },
      readout: percentReadout,
    },
    {
      key: "disk",
      label: "Disk",
      colour: "var(--mm-lane-incoming)",
      value: diskMegabytes(
        now.disk_read_bytes_per_sec,
        now.disk_write_bytes_per_sec,
      ),
      figure: rateFigure,
      unit: " MB/s",
      sub: diskSub(now),
      samples: seriesOf(history, (point) =>
        diskMegabytes(
          point.disk_read_bytes_per_sec,
          point.disk_write_bytes_per_sec,
        ),
      ),
      scale: { kind: "rate", floorTop: DISK_FLOOR_MB },
      readout: rateReadout,
    },
  ];
}

export type MachineTag = { text: string; tone?: "warning" };

/** The pills under the columns: the operating system, how long it has been up, and a reboot waiting for a person. */
export function machineTags(machine: SystemStats["machine"]): MachineTag[] {
  const tags: MachineTag[] = [];
  if (machine.os) tags.push({ text: machine.os });
  if (machine.uptime_seconds !== null)
    tags.push({ text: machineUpWords(machine.uptime_seconds) });
  if (machine.reboot_pending)
    tags.push({ text: "reboot pending", tone: "warning" });
  return tags;
}
