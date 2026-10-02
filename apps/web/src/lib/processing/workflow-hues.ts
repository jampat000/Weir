import { useMemo } from "react";

import type { ProcessingLibrary } from "./libraries-api";
import { useProcessingLibrariesQuery } from "./libraries-queries";

/**
 * A workflow's colour is taken from this palette by its place in Settings › Workflows, and round again after
 * the sixth. They are the hues outside the status colours (green, amber, red). Deluno colours its libraries
 * from the same list in the same way, so a workflow wears one colour in both.
 */
export const WORKFLOW_HUES = [205, 265, 320, 188, 232, 290] as const;

/** What a file whose workflow is not in the list (deleted, or not yet known) is tinted: the palette's first hue. */
export const UNKNOWN_WORKFLOW_HUE: number = WORKFLOW_HUES[0];

/** The hue of the workflow at this place in Settings (0 is the first). */
export function hueAtPosition(position: number): number {
  return WORKFLOW_HUES[Math.max(0, position) % WORKFLOW_HUES.length];
}

/** The workflows in the order Settings › Workflows lists them. */
export function inDisplayOrder(
  libraries: readonly ProcessingLibrary[],
): ProcessingLibrary[] {
  return [...libraries].sort((a, b) => a.display_order - b.display_order);
}

export interface WorkflowHues {
  /** The hue of the workflow with this id; the neutral hue when there is none or it is not in the list. */
  forId: (id: number | null | undefined) => number;
  /** The same by name, where only the name is known (a history row, a shelf tile, a story). */
  forName: (name: string | null | undefined) => number;
}

/** A lookup of every workflow's hue, built from the workflows in the order Settings shows them. */
export function workflowHuesOf(
  libraries: readonly ProcessingLibrary[],
): WorkflowHues {
  const byId = new Map<number, number>();
  const byName = new Map<string, number>();
  inDisplayOrder(libraries).forEach((library, position) => {
    const hue = hueAtPosition(position);
    byId.set(library.id, hue);
    byName.set(library.name, hue);
  });
  return {
    forId: (id) =>
      (id == null ? undefined : byId.get(id)) ?? UNKNOWN_WORKFLOW_HUE,
    forName: (name) =>
      (name == null ? undefined : byName.get(name)) ?? UNKNOWN_WORKFLOW_HUE,
  };
}

/**
 * The hue of each workflow, read from the workflows already cached for Settings and the pages, so a poster does
 * not ask for them again. Until they have loaded every workflow wears the neutral hue.
 */
export function useWorkflowHues(): WorkflowHues {
  const { data } = useProcessingLibrariesQuery();
  return useMemo(() => workflowHuesOf(data ?? []), [data]);
}
