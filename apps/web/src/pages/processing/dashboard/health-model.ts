/** What the Health panel says: each workflow's folder-chain verdict, whether each connection answers, and the tools. */
import type { DownloadClientConnection } from "../../../lib/download-clients/download-clients-api";
import {
  folderChainLines,
  readinessOf,
  type LibraryFolderChain,
  type Readiness,
} from "../../../lib/processing/library-folder-chain-api";
import type { MediaManagerConnection } from "../../../lib/media-managers/media-managers-api";
import type { MediaTools } from "../../../lib/system/media-tools";
import { toolVersion } from "../../../lib/system/media-tools";
import { connectionTitle } from "../../../lib/ui/connection-title";
import { parseAppTime } from "../../../lib/ui/mm-format-date";
import type { MmStatusTone } from "../../../lib/ui/mm-status-tone";
import { ago } from "../processing-words";

export const READINESS_WORDS: Record<Readiness, string> = {
  ready: "In sync",
  not_verified: "Not verified",
  needs_attention: "Needs a fix",
};

const READINESS_TONE: Record<Readiness, MmStatusTone> = {
  ready: "healthy",
  not_verified: "neutral",
  needs_attention: "warning",
};

export type WorkflowVerdict = { words: string; tone: MmStatusTone };

/** A workflow's chain, from its folders to each connection that touches it, as one verdict. */
export function chainVerdict(chain: LibraryFolderChain): WorkflowVerdict {
  const readiness = readinessOf(chain.ready, folderChainLines(chain));
  return { words: READINESS_WORDS[readiness], tone: READINESS_TONE[readiness] };
}

const NOTHING_NAMED = "Open this workflow to see what needs a fix.";

/**
 * The one line that says why a workflow is not in sync: the first problem in its chain, else the first line
 * Weir could only take someone's word for. Null when the chain is in sync.
 */
export function whyNotInSync(chain: LibraryFolderChain): string | null {
  const lines = folderChainLines(chain);
  if (readinessOf(chain.ready, lines) === "ready") return null;
  const named =
    lines.find((line) => line.state === "problem") ??
    lines.find((line) => line.state === "unverified");
  return named?.text ?? NOTHING_NAMED;
}

const CHECKING_VERDICT: WorkflowVerdict = {
  words: "Checking…",
  tone: "neutral",
};
const UNCHECKED_VERDICT: WorkflowVerdict = {
  words: "Could not check",
  tone: "neutral",
};

/** The verdict a check's state gives: the chain's own once it has answered, else that it is still being checked or could not be. */
export function checkVerdict(check: {
  data?: LibraryFolderChain;
  isError: boolean;
}): WorkflowVerdict {
  if (check.data) return chainVerdict(check.data);
  return check.isError ? UNCHECKED_VERDICT : CHECKING_VERDICT;
}

export type ConnectionPill = {
  key: string;
  name: string;
  /** "answered 12s ago", "not answering" or "not tested yet": said in words, never by colour alone. */
  state: string;
  tone: MmStatusTone;
};

type Connection = {
  enabled: boolean;
  last_test_ok?: boolean | null;
  last_test_at?: string | null;
};

const SECONDS_SHOWN_AS_SECONDS = 60;
/** A check this fresh is just now: counting its seconds only flickers. */
const JUST_NOW_SECONDS = 3;

/** How long ago a check was: "just now", then to the second for the first minute ("12s ago"), then "4 min ago". */
export function checkedAgo(
  iso: string | null | undefined,
  now: number,
): string {
  const at = parseAppTime(iso);
  if (at == null) return "";
  const seconds = Math.max(0, Math.round((now - at) / 1000));
  if (seconds < JUST_NOW_SECONDS) return "just now";
  return seconds < SECONDS_SHOWN_AS_SECONDS
    ? `${seconds}s ago`
    : ago(iso as string, now);
}

export function answering(
  connection: Connection,
  now: number,
): Pick<ConnectionPill, "state" | "tone"> {
  if (connection.last_test_ok === true) {
    const when = checkedAgo(connection.last_test_at, now);
    return {
      state: when ? `answered ${when}` : "answering",
      tone: "healthy",
    };
  }
  if (connection.last_test_ok === false)
    return { state: "not answering", tone: "failed" };
  return { state: "not tested yet", tone: "neutral" };
}

/** The media managers and download clients that are switched on, each with whether it answered its last test. */
export function connectionPills(
  managers: readonly MediaManagerConnection[],
  downloadClients: readonly DownloadClientConnection[],
  now: number,
): ConnectionPill[] {
  const pill = (
    kind: "manager" | "client",
    connection: MediaManagerConnection | DownloadClientConnection,
  ): ConnectionPill => ({
    key: `${kind}-${connection.id}`,
    name: connectionTitle(connection),
    ...answering(connection, now),
  });
  return [
    ...managers.filter((m) => m.enabled).map((m) => pill("manager", m)),
    ...downloadClients.filter((c) => c.enabled).map((c) => pill("client", c)),
  ];
}

const NOT_INSTALLED = "not installed";

export type ToolRow = {
  key: "ffmpeg" | "mkvmerge";
  name: string;
  version: string;
  /** The tool's own version line, as it reported it. */
  banner: string;
  tone: MmStatusTone;
};

/**
 * FFmpeg is what Weir cannot work without, so a missing one is a failure. mkvmerge is optional: Weir writes
 * with FFmpeg wherever it is absent, so a missing one is only noted.
 */
export function toolRows(tools: MediaTools): ToolRow[] {
  const missing = (line: string) => line.trim().toLowerCase() === NOT_INSTALLED;
  return [
    {
      key: "ffmpeg",
      name: "FFmpeg",
      version: toolVersion(tools.ffmpeg),
      banner: tools.ffmpeg,
      tone: missing(tools.ffmpeg) ? "failed" : "healthy",
    },
    {
      key: "mkvmerge",
      name: "mkvmerge",
      version: toolVersion(tools.mkvmerge),
      banner: tools.mkvmerge,
      tone: missing(tools.mkvmerge) ? "neutral" : "healthy",
    },
  ];
}

const isProblem = (tone: MmStatusTone) =>
  tone === "warning" || tone === "failed";

/**
 * The few words beside the panel's title: how many things need a look, else how many workflows Weir could not
 * verify, else that all is clear. A workflow Weir takes someone's word for is not a fault, but it is not clear either.
 */
export function healthSummary(
  problems: number,
  workflows: readonly { verdict: WorkflowVerdict }[],
): string {
  if (problems > 0) return `${problems} to look at`;
  const unverified = workflows.filter(
    (item) => item.verdict.words === READINESS_WORDS.not_verified,
  ).length;
  return unverified > 0 ? `${unverified} not verified` : "all clear";
}

/** How many things in the panel need a look: a workflow that needs a fix, a connection or a tool that is down. */
export function problemCount(parts: {
  workflows: readonly { verdict: WorkflowVerdict }[];
  connections: readonly ConnectionPill[];
  tools: readonly ToolRow[] | null;
}): number {
  return (
    parts.workflows.filter((item) => isProblem(item.verdict.tone)).length +
    parts.connections.filter((pill) => isProblem(pill.tone)).length +
    (parts.tools ?? []).filter((tool) => isProblem(tool.tone)).length
  );
}
