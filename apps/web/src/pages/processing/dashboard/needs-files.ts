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
/** A file failed, turned away or held changes only when Weir looks at it, so the list follows the stream gently. */
const STREAM_THROTTLE_MS = 3_000;

/**
 * The files that may wait on a person: failed, rejected, held and skipped, newest first. Which of them do
 * is the model's to say.
 */
export function useNeedsFiles(
  workflowId: number | null | undefined,
): ProcessingFile[] {
  const { query, queryKeys } = useMemo(() => {
    const read = {
      file_status: [...NEEDS_FILE_STATUSES],
      library_id: workflowId ?? undefined,
      limit: NEEDS_FILES_READ,
    };
    return { query: read, queryKeys: [processingKeys.fileList(read)] as const };
  }, [workflowId]);
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
