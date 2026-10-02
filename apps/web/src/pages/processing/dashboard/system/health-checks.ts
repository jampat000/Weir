/**
 * The checks the Health card lists, one per thing Weir can look at: each switched-on workflow's folder chain, each
 * connection, each tool, each drive Weir writes to, the backups and Weir itself. Each says whether it passes, why in
 * a sentence, when it was last looked at and what to do about it. The card only draws what these return.
 */
import {
  answerWords,
  type ConnectionEntry,
} from "../../../../lib/connections/connection-model";
import { formatBytes } from "../../../../lib/format/bytes";
import type { SystemDrive } from "../../../../lib/system/system-stats-types";
import { plural } from "../../../../lib/ui/mm-plural";
import type { ToolRow } from "../health-model";
import type { WorkflowHealth } from "../use-health";
import {
  ABOUT_PATH,
  BACKUPS_PATH,
  MANAGERS_PATH,
  workflowPath,
} from "./system-paths";

export type HealthArea =
  "workflows" | "connections" | "tools" | "storage" | "backups" | "weir";

export const HEALTH_AREAS: readonly { key: HealthArea; name: string }[] = [
  { key: "workflows", name: "Workflows" },
  { key: "connections", name: "Connections" },
  { key: "tools", name: "Tools" },
  { key: "storage", name: "Storage" },
  { key: "backups", name: "Backups" },
  { key: "weir", name: "Weir" },
];

/**
 * `bad` and `warn` need you; `ok` passes; `idle` has not been proven either way (a chain Weir could only take on
 * trust, a connection never tested); `note` is a fact worth reading that is not a check, so it is not counted.
 */
export type CheckTone = "bad" | "warn" | "ok" | "idle" | "note";

export type CheckFix = {
  label: string;
  to: string;
  /** What pressing it does, on hover. */
  note: string;
};

export type HealthCheck = {
  id: string;
  area: HealthArea;
  tone: CheckTone;
  title: string;
  why: string;
  /** When it was last looked at, in ms since the epoch. */
  checkedAt: number | null;
  fix: CheckFix | null;
  /** What "Check again" looks at again: the check's area, and which one in it for a workflow or a connection. */
  again: { area: HealthArea; key: string | null };
  /** The workflow this check is about, whose folder chain "Details" opens. */
  workflowId: number | null;
};

const FIX_LABEL = "Fix it →";
/** A drive that fills in this many days is worth a look before it does. */
const FILLS_SOON_DAYS = 7;
/** A backup is overdue when it is older than this many times the interval between backups. */
const OVERDUE_INTERVALS = 2;
const MS_PER_HOUR = 3_600_000;

const link = (to: string, note: string): CheckFix => ({
  label: FIX_LABEL,
  to,
  note,
});

export function workflowChecks(
  workflows: readonly WorkflowHealth[],
): HealthCheck[] {
  return workflows.map((item) => {
    const { workflow, verdict, why } = item;
    const tone: CheckTone =
      verdict.tone === "healthy"
        ? "ok"
        : verdict.tone === "warning"
          ? "warn"
          : "idle";
    return {
      id: `workflow:${workflow.id}`,
      area: "workflows",
      tone,
      title: tone === "warn" ? `${workflow.name} needs a fix` : workflow.name,
      why:
        tone === "ok"
          ? "Its folders and connections are in sync."
          : (why ?? verdict.words),
      checkedAt: item.checkedAt,
      fix:
        tone === "warn" || tone === "idle"
          ? link(workflowPath(workflow.id), "Opens this workflow's settings.")
          : null,
      again: { area: "workflows", key: String(workflow.id) },
      workflowId: workflow.id,
    };
  });
}

function connectionCheck(entry: ConnectionEntry): HealthCheck {
  const base = {
    id: `connection:${entry.key}`,
    area: "connections" as const,
    checkedAt: entry.checkedAt,
    again: { area: "connections" as const, key: entry.key },
    workflowId: null,
  };
  const fix = link(
    MANAGERS_PATH,
    "Opens the media managers and download clients.",
  );
  const took =
    entry.answerMs === null ? "" : ` in ${answerWords(entry.answerMs)}`;
  switch (entry.state) {
    case "down":
      return {
        ...base,
        tone: "bad",
        title: `${entry.name} isn't answering`,
        why:
          entry.detail ||
          "Weir could not reach it. Check its address and that it is running.",
        fix,
      };
    case "slow":
      return {
        ...base,
        tone: "warn",
        title: `${entry.name} is slow to answer`,
        why: `Its last answer came${took}.`,
        fix,
      };
    case "untested":
      return {
        ...base,
        tone: "idle",
        title: entry.name,
        why: "Weir has not tested it yet.",
        fix: null,
      };
    default:
      return {
        ...base,
        tone: "ok",
        title: entry.name,
        why: `Answering${took}.`,
        fix: null,
      };
  }
}

/** Each switched-on media manager and download client. */
export function connectionChecks(
  entries: readonly ConnectionEntry[],
): HealthCheck[] {
  return entries.filter((entry) => entry.enabled).map(connectionCheck);
}

export function toolChecks(
  tools: readonly ToolRow[] | null,
  checkedAt: number | null,
): HealthCheck[] {
  return (tools ?? []).map((tool): HealthCheck => {
    const base = {
      id: `tool:${tool.key}`,
      area: "tools" as const,
      checkedAt,
      again: { area: "tools" as const, key: null },
      workflowId: null,
    };
    if (tool.tone === "failed") {
      return {
        ...base,
        tone: "bad",
        title: `${tool.name} is missing`,
        why: "Weir cannot process files without it.",
        fix: link(ABOUT_PATH, "Opens System, where the tools are listed."),
      };
    }
    if (tool.tone === "neutral") {
      return {
        ...base,
        tone: "note",
        title: `${tool.name} is not installed`,
        why: "It is optional: Weir writes with FFmpeg where it is missing.",
        fix: null,
      };
    }
    return {
      ...base,
      tone: "ok",
      title: tool.name,
      why: tool.banner,
      fix: null,
    };
  });
}

function driveCheck(drive: SystemDrive, checkedAt: number | null): HealthCheck {
  const free = formatBytes(drive.free_bytes);
  const first = drive.workflows[0];
  const fix = first
    ? link(
        workflowPath(first.id),
        "Opens the settings of a workflow that writes here.",
      )
    : null;
  const base = {
    id: `drive:${drive.path}`,
    area: "storage" as const,
    checkedAt,
    again: { area: "storage" as const, key: null },
    workflowId: null,
  };
  if (drive.keep_free_bytes > 0 && drive.free_bytes < drive.keep_free_bytes) {
    return {
      ...base,
      tone: "bad",
      title: `${drive.name} is low on space`,
      why: `${free} free. Weir keeps ${formatBytes(drive.keep_free_bytes)} free here, so it holds new files until there is room.`,
      fix,
    };
  }
  if (drive.full_in_days !== null && drive.full_in_days <= FILLS_SOON_DAYS) {
    return {
      ...base,
      tone: "warn",
      title: `${drive.name} fills up soon`,
      why: `${free} free. At this pace it is full in about ${plural(Math.max(1, Math.round(drive.full_in_days)), "day", "days")}.`,
      fix,
    };
  }
  return {
    ...base,
    tone: "ok",
    title: drive.name,
    why: `${free} free.`,
    fix: null,
  };
}

/** Each drive any workflow reads from or writes to. */
export function storageChecks(
  drives: readonly SystemDrive[] | null,
  checkedAt: number | null,
): HealthCheck[] {
  return (drives ?? []).map((drive) => driveCheck(drive, checkedAt));
}

export type BackupFacts = {
  enabled: boolean;
  intervalHours: number;
  /** The newest backup, made by hand or by itself, in ms since the epoch. */
  lastBackupAt: number | null;
  checkedAt: number | null;
};

/** The configuration backup: switched on, and recent enough for the interval it is set to. */
export function backupChecks(
  backups: BackupFacts | null,
  now: number,
): HealthCheck[] {
  if (backups === null) return [];
  const base = {
    id: "backups:configuration",
    area: "backups" as const,
    title: "Configuration backup",
    checkedAt: backups.checkedAt,
    again: { area: "backups" as const, key: null },
    workflowId: null,
  };
  const fix = link(BACKUPS_PATH, "Opens Backups.");
  if (!backups.enabled) {
    return [
      {
        ...base,
        tone: "warn",
        why: "Automatic backups are off, so your settings are saved only when you back up by hand.",
        fix,
      },
    ];
  }
  const overdueMs = backups.intervalHours * OVERDUE_INTERVALS * MS_PER_HOUR;
  if (backups.lastBackupAt === null || now - backups.lastBackupAt > overdueMs) {
    return [
      {
        ...base,
        tone: "warn",
        why:
          backups.lastBackupAt === null
            ? "No backup has been made yet."
            : "The last backup is older than the schedule allows.",
        fix,
      },
    ];
  }
  return [{ ...base, tone: "ok", why: "Backing up on schedule.", fix: null }];
}

export type WeirFacts = {
  /** What each worker that has stopped says, one string per worker. */
  stoppedWorkers: readonly string[];
  /** The newer version waiting, when there is one. */
  updateVersion: string | null;
  checkedAt: number | null;
};

/** Weir's own background workers, and a newer version when one is out. */
export function weirChecks(weir: WeirFacts | null): HealthCheck[] {
  if (weir === null) return [];
  const base = {
    area: "weir" as const,
    checkedAt: weir.checkedAt,
    again: { area: "weir" as const, key: null },
    workflowId: null,
  };
  const workers: HealthCheck =
    weir.stoppedWorkers.length > 0
      ? {
          ...base,
          id: "weir:workers",
          tone: "bad",
          title: "A background worker has stopped",
          why: weir.stoppedWorkers[0],
          fix: link(ABOUT_PATH, "Opens System."),
        }
      : {
          ...base,
          id: "weir:workers",
          tone: "ok",
          title: "Background workers",
          why: "Every worker is running.",
          fix: null,
        };
  if (weir.updateVersion === null) return [workers];
  return [
    workers,
    {
      ...base,
      id: "weir:update",
      tone: "note",
      title: `Weir ${weir.updateVersion} is out`,
      why: "A newer version is ready to install.",
      fix: link(ABOUT_PATH, "Opens System, where updates are installed."),
    },
  ];
}
