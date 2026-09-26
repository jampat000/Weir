import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

import { fetchKeptFiles, processKeptFileAgain } from "./kept-files-api";
import { processingKeys } from "./query-keys";

export function useKeptFilesQuery() {
  return useQuery({
    queryKey: processingKeys.keptFiles,
    queryFn: fetchKeptFiles,
  });
}

export function useProcessKeptFileAgain() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => processKeptFileAgain(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.keptFiles }),
  });
}
