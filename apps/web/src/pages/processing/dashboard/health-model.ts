/** What the Health panel says: each workflow's folder-chain verdict, whether each connection answers, and the tools. */
import {
  needsALook,
  type ConnectionEntry,
} from "../../../lib/connections/connection-model";
import {
  READINESS_MEANING,
  folderChainLines,
  readinessOf,
  type LibraryFolderChain,
  type Readiness,
} from "../../../lib/processing/library-folder-chain-api";
import type { MediaTools } from "../../../lib/system/media-tools";
import { toolVersion } from "../../../lib/system/media-tools";
import { needsYou, type StatusMeaning } from "../../../lib/ui/status-meaning";
import { ago } from "../processing-words";

export const READINESS_WORDS: Record<Readiness, string> = {
  ready: "In sync",
  not_verified: "Not verified",
  needs_attention: "Needs a fix",
};

export type WorkflowVerdict = {
  words: string;
  meaning: StatusMeaning;
  /** How the chain stands once it has been read; null while it is being checked or could not be. */
  readiness: Readiness | null;
};

/** A workflow's chain, from its folders to each connection that touches it, as one verdict. */
export function chainVerdict(chain: LibraryFolderChain): WorkflowVerdict {
  const readiness = readinessOf(chain.ready, folderChainLines(chain));
  return {
    words: READINESS_WORDS[readiness],
    meaning: READINESS_MEANING[readiness],
    readiness,
  };
}

const NOTHING_NAMED = "Open it to see what to fix.";

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

/** What a workflow's verdict says while its folder check has not answered. */
export const CHECKING_WORDS = "Checking…";
const CHECKING_VERDICT: WorkflowVerdict = {
  words: CHECKING_WORDS,
  meaning: "doing",
  readiness: null,
};
const UNCHECKED_VERDICT: WorkflowVerdict = {
  words: "Couldn't check",
  meaning: "broken",
  readiness: null,
};

/** The verdict a check's state gives: the chain's own once it has answered, else that it is still being checked or could not be. */
export function checkVerdict(check: {
  data?: LibraryFolderChain;
  isError: boolean;
}): WorkflowVerdict {
  if (check.data) return chainVerdict(check.data);
  return check.isError ? UNCHECKED_VERDICT : CHECKING_VERDICT;
}

const SECONDS_SHOWN_AS_SECONDS = 60;
/** A check this fresh is just now: counting its seconds only flickers. */
const JUST_NOW_SECONDS = 3;

/** How long ago something happened: "just now", then to the second for the first minute ("12s ago"), then "4 min ago". */
export function checkedAgo(at: number | null, now: number): string {
  if (at === null) return "";
  const seconds = Math.max(0, Math.round((now - at) / 1000));
  if (seconds < JUST_NOW_SECONDS) return "just now";
  return seconds < SECONDS_SHOWN_AS_SECONDS
    ? `${seconds}s ago`
    : ago(new Date(at).toISOString(), now);
}

/**
 * What the Health panel's fitting sees, in the order it lists them: a heading, a row of a list, a note that stands in
 * for a list with nothing in it, or the whole Tools section.
 */
export type HealthUnit = "heading" | "row" | "note" | "tools";

/** How many rows are left out when only the first `fits` units show. */
export function rowsLeftOut(
  units: readonly HealthUnit[],
  fits: number,
): number {
  return units.slice(Math.max(0, fits)).filter((unit) => unit === "row").length;
}

const NOT_INSTALLED = "not installed";

export type ToolRow = {
  key: "ffmpeg" | "mkvmerge";
  name: string;
  version: string;
  /** The tool's own version line, as it reported it. */
  banner: string;
  meaning: StatusMeaning;
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
      meaning: missing(tools.ffmpeg) ? "broken" : "done",
    },
    {
      key: "mkvmerge",
      name: "mkvmerge",
      version: toolVersion(tools.mkvmerge),
      banner: tools.mkvmerge,
      meaning: missing(tools.mkvmerge) ? "idle" : "done",
    },
  ];
}

/** The few words beside the panel's title: how many things need a look, else that all is clear. */
export function healthSummary(problems: number): string {
  return problems > 0 ? `${problems} to look at` : "all clear";
}

/** How many things in the panel need a look: a workflow that needs a fix or is not verified, a connection that is down or slow, a tool that is down. */
export function problemCount(parts: {
  workflows: readonly { verdict: WorkflowVerdict }[];
  connections: readonly ConnectionEntry[];
  tools: readonly ToolRow[] | null;
}): number {
  return (
    parts.workflows.filter((item) => needsYou(item.verdict.meaning)).length +
    parts.connections.filter((entry) => entry.enabled && needsALook(entry))
      .length +
    (parts.tools ?? []).filter((tool) => needsYou(tool.meaning)).length
  );
}
