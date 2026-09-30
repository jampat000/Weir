import { useMutation, useQueryClient } from "@tanstack/react-query";

import {
  fetchRejectedFilesSummary,
  processRejectedFilesAgain,
} from "./rejected-files-api";
import { processingKeys } from "./query-keys";

/** A mutation rather than a query: the count is read when someone asks, so the dialog never quotes a stale one. */
export function useRejectedFilesSummary() {
  return useMutation({
    mutationFn: (libraryId?: number) => fetchRejectedFilesSummary(libraryId),
  });
}

export function useProcessRejectedFilesAgain() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (libraryId?: number) => processRejectedFilesAgain(libraryId),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}
