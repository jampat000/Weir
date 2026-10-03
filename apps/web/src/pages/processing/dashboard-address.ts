import { useCallback } from "react";
import { useSearchParams } from "react-router-dom";

import type { Filter } from "./processing-filter";

/** The Dashboard's two views: what is happening now, and how Weir's setup and background work are doing. */
export type DashboardView = "live" | "system";

const VIEW_PARAM = "view";
const WORKFLOW_PARAM = "workflow";
const WORK_PARAM = "work";

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

/** The kind of work a link narrows the Live view to. An unknown or missing `?work=` is everything, which carries no parameter. */
export function filterFromSearch(params: URLSearchParams): Filter {
  const work = params.get(WORK_PARAM);
  return work === "download" || work === "library" ? work : "all";
}

export type DashboardAddress = {
  view: DashboardView;
  setView: (view: DashboardView) => void;
  /** The workflow the page is narrowed to, or null for every workflow. */
  workflowId: number | null;
  setWorkflowId: (id: number | null) => void;
  /** The kind of work Live is narrowed to. System ignores it, and it stays in the address while System is shown. */
  filter: Filter;
  setFilter: (filter: Filter) => void;
};

/**
 * The view, the workflow and the kind of work picked, kept in the address (`?view=system`, `?workflow=3`,
 * `?work=library`) so a view can be linked and survives a reload. Each leaves the others, and any other
 * parameter, as it is.
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
  // Choosing a workflow or a kind of work is a filter, not a place, so Back does not step through every choice.
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

  const setFilter = useCallback(
    (filter: Filter) =>
      setParams(
        (current) => {
          const next = new URLSearchParams(current);
          if (filter === "all") next.delete(WORK_PARAM);
          else next.set(WORK_PARAM, filter);
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
    filter: filterFromSearch(params),
    setFilter,
  };
}
