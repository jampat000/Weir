import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

/** The Dashboard's two views: what is happening now, and how Weir's setup and background work are doing. */
export type DashboardView = "live" | "system";

const VIEW_PARAM = "view";
const WORKFLOW_PARAM = "workflow";

/** An unknown or missing `?view=` is Live, which carries no parameter at all. */
export function viewFromSearch(params: URLSearchParams): DashboardView {
  return params.get(VIEW_PARAM) === "system" ? "system" : "live";
}

/** The workflow a link narrows the page to, or null for every workflow. Anything but a whole id is ignored. */
export function workflowFromSearch(params: URLSearchParams): number | null {
  const raw = params.get(WORKFLOW_PARAM) ?? "";
  if (!/^[1-9]\d{0,9}$/.test(raw)) return null;
  return Number(raw);
}

/**
 * The workflow to narrow to once the list of workflows is known. One that is switched off, deleted or
 * mistyped in a link shows everything; while the list is still loading the link's own choice stands, so
 * the page does not flash every workflow first.
 */
export function resolveWorkflow(
  requested: number | null,
  known: readonly { id: number }[] | undefined,
): number | null {
  if (requested === null || known === undefined) return requested;
  return known.some((workflow) => workflow.id === requested) ? requested : null;
}

export type DashboardAddress = {
  view: DashboardView;
  setView: (view: DashboardView) => void;
  /** The workflow the page is narrowed to, or null for every workflow. */
  workflowId: number | null;
  setWorkflowId: (id: number | null) => void;
};

/**
 * The view and the workflow picked, kept in the address (`?view=system`, `?workflow=3`) so a view can be
 * linked and survives a reload. Each leaves the other, and any other parameter, as it is.
 */
export function useDashboardAddress(
  enabledWorkflows: readonly { id: number }[] | undefined,
): DashboardAddress {
  const [params, setParams] = useSearchParams();

  const setView = useCallback(
    (view: DashboardView) =>
      setParams((current) => {
        const next = new URLSearchParams(current);
        if (view === "live") next.delete(VIEW_PARAM);
        else next.set(VIEW_PARAM, view);
        return next;
      }),
    [setParams],
  );
  // Choosing a workflow is a filter, not a place, so Back does not step through every choice.
  const setWorkflowId = useCallback(
    (id: number | null) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (id === null) next.delete(WORKFLOW_PARAM);
          else next.set(WORKFLOW_PARAM, String(id));
          return next;
        },
        { replace: true },
      ),
    [setParams],
  );

  return {
    view: viewFromSearch(params),
    setView,
    workflowId: resolveWorkflow(workflowFromSearch(params), enabledWorkflows),
    setWorkflowId,
  };
}
