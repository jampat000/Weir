/** What the This Weir card says: the health ring's figures and the fact tiles beside it, from how Weir itself is running. */
import { plural } from "../../../../lib/ui/mm-plural";
import type {
  RunsAs,
  SystemOverview,
} from "../../../../lib/system/system-stats-types";
import { parseAppTime } from "../../../../lib/ui/mm-format-date";
import { ago } from "../../processing-words";
import { sizeWords, splitAddress, uptimeWords } from "./system-words";

export type FactTone = "good" | "bad";

export type Fact = {
  key: string;
  label: string;
  value: string;
  sub: string;
  tone?: FactTone;
};

export type RingFigures = {
  /** How much of the ring is filled, 0 to 1. */
  fraction: number;
  passing: number;
  total: number;
  /** Checks that do not pass: what needs a person. */
  needYou: number;
};

/** The checks the ring counts: those that pass, all of them, and those that need a person (a few are neither). */
export type RingChecks = { passing: number; total: number; need: number };

/** The ring's figures from the checks: no checks at all is a full ring, since nothing is wrong. */
export function ringFigures({ passing, total, need }: RingChecks): RingFigures {
  const count = Math.max(0, total);
  const pass = Math.min(Math.max(0, passing), count);
  return {
    fraction: count === 0 ? 1 : pass / count,
    passing: pass,
    total: count,
    needYou: Math.max(0, need),
  };
}

const RUNS_AS_WORDS: Record<RunsAs, { value: string; sub: string }> = {
  service: { value: "Service", sub: "in the background" },
  app: { value: "App", sub: "while signed in" },
  docker: { value: "Docker", sub: "in a container" },
};

const UPDATE_WORDS: Record<SystemOverview["update"]["status"], string> = {
  checking: "checking",
  up_to_date: "up to date",
  update_available: "update ready",
  downloaded: "update ready",
  not_published: "no release yet",
  unavailable: "cannot check",
};

function updateFact(
  update: SystemOverview["update"],
): Pick<Fact, "sub" | "tone"> {
  const ready =
    update.status === "update_available" || update.status === "downloaded";
  if (ready && update.latest_version) {
    return { sub: `${update.latest_version} ready` };
  }
  return {
    sub: UPDATE_WORDS[update.status],
    tone: update.status === "up_to_date" ? "good" : undefined,
  };
}

/** How the server's own answers read: the median to the millisecond, and what nearly all requests beat. */
function answersFact(
  requests: SystemOverview["requests"],
): Pick<Fact, "value" | "sub"> {
  return {
    value: `${Math.round(requests.median_ms)} ms`,
    sub: `95% under ${Math.round(requests.p95_ms)} ms`,
  };
}

function backupSub(at: string | null, bytes: number | null): string {
  if (at === null) return "back up in Backups";
  return bytes === null ? "" : sizeWords(bytes);
}

export type FactsInput = {
  overview: SystemOverview;
  /** Passes running and the slots Weir has for them, from the newest reading; null before it arrives. */
  work: { running: number; slots: number } | null;
  /** When the newest configuration backup was made, or null when there is none. */
  lastBackupAt: string | null;
  lastBackupBytes: number | null;
  now: number;
};

/** The fact tiles, in the order they read. Every one is a reading; none is made up. */
export function weirFacts({
  overview,
  work,
  lastBackupAt,
  lastBackupBytes,
  now,
}: FactsInput): Fact[] {
  const { host, port } = splitAddress(overview.address);
  const runsAs = RUNS_AS_WORDS[overview.runs_as];
  const failed = overview.jobs_today.failed;
  const startedAt = Date.parse(overview.started_at);
  const uptimeSeconds = Number.isNaN(startedAt)
    ? overview.uptime_seconds
    : Math.max(0, (now - startedAt) / 1000);
  return [
    {
      key: "version",
      label: "Version",
      value: overview.version,
      ...updateFact(overview.update),
    },
    {
      key: "uptime",
      label: "Uptime",
      value: uptimeWords(uptimeSeconds),
      sub:
        overview.restarts_this_week > 0
          ? plural(overview.restarts_this_week, "restart", "restarts")
          : "no restarts",
      tone: overview.restarts_this_week > 0 ? "bad" : "good",
    },
    {
      key: "files-at-once",
      label: "Files at once",
      value: work ? `${work.running} / ${work.slots}` : "–",
      sub: work && work.running > 0 ? "running" : "idle",
    },
    {
      key: "jobs",
      label: "Jobs today",
      value: `${overview.jobs_today.run.toLocaleString()} run`,
      sub: failed > 0 ? `${failed.toLocaleString()} failed` : "none failed",
      tone: failed > 0 ? "bad" : "good",
    },
    { key: "answers", label: "Answers in", ...answersFact(overview.requests) },
    {
      key: "backup",
      label: "Last backup",
      value: lastBackupAt ? ago(lastBackupAt, now) : "None yet",
      sub: backupSub(lastBackupAt, lastBackupBytes),
    },
    { key: "runs-as", label: "Runs as", ...runsAs },
    {
      key: "address",
      label: "Address",
      value: host || "–",
      sub: port ? `port ${port}` : "",
    },
    {
      key: "data",
      label: "Data",
      value: sizeWords(overview.data_bytes),
      sub: "settings, history",
    },
    {
      key: "browsers",
      label: "Browsers",
      value: overview.browsers_live.toLocaleString(),
      sub: "open now",
    },
  ];
}

export type BackupEntry = { created_at: string; size_bytes: number };

/** The most recent backup of a list that may be in any order; null when there are none. */
export function newestBackup<T extends BackupEntry>(
  items: readonly T[],
): T | null {
  return items.reduce<T | null>(
    (latest, item) =>
      latest === null ||
      (parseAppTime(item.created_at) ?? 0) >
        (parseAppTime(latest.created_at) ?? 0)
        ? item
        : latest,
    null,
  );
}
