import { useQuery } from "@tanstack/react-query";

import {
  fetchProcessingFiles,
  type ProcessingFileStatus,
  type ProcessingFilesQuery,
} from "./files-api";
import { processingKeys } from "./query-keys";

/** The files that wait on a person: a pass that failed, and a download its media manager turned down. */
const NEEDS_YOU_STATUSES: readonly ProcessingFileStatus[] = [
  "processing_failed",
  "rejected",
];

/** The count rides on every files page, so one row is all that is read. */
const COUNT_PROBE: ProcessingFilesQuery = { limit: 1 };

function probeFor(workflowId: number | null): ProcessingFilesQuery {
  return workflowId === null
    ? COUNT_PROBE
    : { ...COUNT_PROBE, library_id: workflowId };
}

const COUNT_REFRESH_MS = 30_000;

/** How many files wait on a person, from the per-status counts the files endpoint sends. */
export function needsYouCount(
  statusCounts: Record<string, number> | undefined,
): number {
  return NEEDS_YOU_STATUSES.reduce(
    (total, status) => total + (statusCounts?.[status] ?? 0),
    0,
  );
}

/**
 * For the sidebar, beside History: the files that need someone to look. Given a workflow, only its files;
 * the sidebar counts every workflow's.
 */
export function useNeedsYouCount(workflowId: number | null = null): number {
  const probe = probeFor(workflowId);
  const files = useQuery({
    queryKey: processingKeys.fileList(probe),
    queryFn: () => fetchProcessingFiles(probe),
    refetchInterval: COUNT_REFRESH_MS,
  });
  return needsYouCount(files.data?.status_counts);
}
