import type { FetcherJobsInspectionFilter } from "./queries";

/**
 * Visible labels for the Failed imports Job history “Show” control only.
 * Values stay aligned with the API; wording is product-facing.
 */
export const FETCHER_JOBS_INSPECTION_FILTER_OPTIONS: { value: FetcherJobsInspectionFilter; label: string }[] = [
  { value: "terminal", label: "Finished" },
  { value: "handler_ok_finalize_failed", label: "Needs follow-up" },
  { value: "failed", label: "Stopped with errors" },
  { value: "completed", label: "Completed" },
  { value: "pending", label: "Waiting to start" },
  { value: "leased", label: "Running now" },
];
