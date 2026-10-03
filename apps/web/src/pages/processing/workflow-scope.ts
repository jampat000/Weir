import type { LeavingCard } from "./leaving-cards";

/** The cards of files that have just ended which belong to the chosen workflow; every card when none is chosen. */
export function leavingInWorkflow(
  leaving: readonly LeavingCard[],
  workflowId: number | null,
): readonly LeavingCard[] {
  return workflowId === null
    ? leaving
    : leaving.filter((card) => card.file.library_id === workflowId);
}
