import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { useMemo } from "react";

import { useActivityStreamInvalidations } from "../../../lib/activity/use-activity-stream-invalidation";
import {
  fetchProcessingFiles,
  type ProcessingFile,
} from "../../../lib/processing/files-api";
import { processingKeys } from "../../../lib/processing/query-keys";
import { NEEDS_FILES_READ, NEEDS_FILE_STATUSES } from "./needs-model";

const NO_FILES: ProcessingFile[] = [];
/** A file turned away or held changes only when Weir looks at it, so the list follows the stream gently. */
const STREAM_THROTTLE_MS = 3_000;

/**
 * The files besides the failed ones that may wait on a person: rejected, held and skipped, newest first.
 * Which of them do is the model's to say. The page's own count of rejected files is part of the key, so a
 * rejection arriving or leaving is a new read rather than a wait for the stream.
 */
export function useNeedsFiles(
  workflowId: number | null | undefined,
  rejectedCount: number,
): ProcessingFile[] {
  const { query, queryKeys } = useMemo(() => {
    const read = {
      file_status: [...NEEDS_FILE_STATUSES],
      library_id: workflowId ?? undefined,
      limit: NEEDS_FILES_READ,
    };
    return {
      query: read,
      queryKeys: [[...processingKeys.fileList(read), rejectedCount]] as const,
    };
  }, [workflowId, rejectedCount]);
  const files = useQuery({
    queryKey: queryKeys[0],
    queryFn: () => fetchProcessingFiles(query),
    placeholderData: keepPreviousData,
  });
  useActivityStreamInvalidations(queryKeys, {
    exact: true,
    throttleMs: STREAM_THROTTLE_MS,
  });
  return files.data?.files ?? NO_FILES;
}
