import type {
  MediaManagerConnection,
  MediaManagerKind,
} from "../media-managers/media-managers-api";
import type { ProcessingLibrary } from "./libraries-api";

/**
 * Every workflow is one of two kinds, and every page says so in the same words: Weir only (Weir watches a
 * folder and writes cleaned files to an output folder, and nothing else is involved), or linked to a media
 * manager (it hands files over or imports the result, and Weir reports back). Both can exist at once.
 * A bare download client only suggests a watched folder, so it never makes a workflow linked.
 */
export type WorkflowManager = {
  id: number;
  name: string;
  kind: MediaManagerKind | null;
};

export type WorkflowKind =
  { kind: "weir_only" } | { kind: "linked"; managers: WorkflowManager[] };

const REMOVED_MANAGER = "a removed media manager";

export function workflowKindOf(
  workflow: Pick<ProcessingLibrary, "manager_connection_ids">,
  connections: MediaManagerConnection[],
): WorkflowKind {
  if (workflow.manager_connection_ids.length === 0) {
    return { kind: "weir_only" };
  }
  return {
    kind: "linked",
    managers: workflow.manager_connection_ids.map((id) => {
      const connection = connections.find((c) => c.id === id);
      return {
        id,
        name: connection?.name ?? REMOVED_MANAGER,
        kind: connection?.kind ?? null,
      };
    }),
  };
}

export const WEIR_ONLY_LABEL = "Weir only";

function joinNames(names: string[]): string {
  if (names.length <= 1) return names.join("");
  return `${names.slice(0, -1).join(", ")} and ${names[names.length - 1]}`;
}

/** The badge: "Weir only" or "Linked to Deluno". */
export function workflowBadgeLabel(kind: WorkflowKind): string {
  return kind.kind === "weir_only"
    ? WEIR_ONLY_LABEL
    : `Linked to ${joinNames(kind.managers.map((m) => m.name))}`;
}

function handOff(manager: WorkflowManager): string {
  switch (manager.kind) {
    case "deluno":
      return `${manager.name} hands each finished download to Weir, then imports the cleaned file.`;
    case "sonarr":
    case "radarr":
      return `Weir watches the folder ${manager.name} downloads to, and ${manager.name} imports the cleaned files.`;
    default:
      return `${manager.name} sends files to Weir and hears back when they are cleaned.`;
  }
}

/** One sentence on how files arrive and where they go, for the row or heading under the badge. */
export function workflowKindNote(kind: WorkflowKind): string {
  return kind.kind === "weir_only"
    ? "Files that appear in the watched folder are cleaned into the output folder. Nothing else is involved."
    : kind.managers.map(handOff).join(" ");
}

/** How many workflows of each kind, for a line above a list: "2 linked, 1 Weir only". */
export function workflowKindCounts(kinds: WorkflowKind[]): string {
  const linked = kinds.filter((kind) => kind.kind === "linked").length;
  const weirOnly = kinds.length - linked;
  return [
    linked > 0 ? `${linked} linked to a media manager` : null,
    weirOnly > 0 ? `${weirOnly} ${WEIR_ONLY_LABEL}` : null,
  ]
    .filter((part): part is string => part !== null)
    .join(" · ");
}
