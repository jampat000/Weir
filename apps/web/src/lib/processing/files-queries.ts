import {
  keepPreviousData,
  useMutation,
  useQuery,
  useQueryClient,
} from "@tanstack/react-query";

import { postProcessingFileRemuxPassEnqueue } from "./file-remux-pass-api";
import {
  fetchProcessingFiles,
  forgetProcessingFile,
  fetchProcessingFileLog,
  fetchProcessingFileTracks,
  fetchProcessingWhyHeld,
  moveProcessingFileToTop,
  postProcessingManualPlan,
  requeueProcessingFile,
  requeueProcessingFiles,
  type ProcessingBulkRequeueQuery,
  type ProcessingFilesPage,
  type ProcessingFilesQuery,
  type ProcessingManualPlanChoice,
} from "./files-api";
import {
  fetchLibraryCleans,
  type LibraryCleansQuery,
} from "./library-cleans-api";
import { postProcessingWatchedFolderRemuxScanDispatchEnqueue } from "./watched-folder-scan-api";
import { processingKeys } from "./query-keys";

export function useProcessingFilesQuery(query: ProcessingFilesQuery = {}) {
  return useQuery<ProcessingFilesPage>({
    queryKey: processingKeys.fileList(query),
    queryFn: () => fetchProcessingFiles(query),
  });
}

/** How often History follows a running pass. */
const HISTORY_REFRESH_MS = 5000;

/**
 * History's downloads. The list on screen stays while a new filter loads, and it refreshes by itself only while a
 * file is being processed: a queued file changes nothing worth a thousand-row read until it starts (#719).
 */
export function useFileHistoryQuery(query: ProcessingFilesQuery) {
  return useQuery<ProcessingFilesPage>({
    queryKey: processingKeys.fileList(query),
    queryFn: () => fetchProcessingFiles(query),
    placeholderData: keepPreviousData,
    refetchInterval: (q) =>
      q.state.data?.files.some((file) => file.status === "processing")
        ? HISTORY_REFRESH_MS
        : false,
  });
}

/** History's library cleans (#695), kept on screen while a new filter loads. */
export function useLibraryCleansQuery(query: LibraryCleansQuery) {
  return useQuery({
    queryKey: processingKeys.libraryCleans(query),
    queryFn: () => fetchLibraryCleans(query),
    placeholderData: keepPreviousData,
  });
}

export function useForgetProcessingFile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => forgetProcessingFile(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useMoveProcessingFileToTop() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => moveProcessingFileToTop(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useRequeueProcessingFile() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => requeueProcessingFile(id),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useRequeueProcessingFiles() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (query: ProcessingBulkRequeueQuery) =>
      requeueProcessingFiles(query),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useProcessingWhyHeld() {
  // A mutation rather than a query: this asks the managers *right now*, and only when
  // someone asks. Running it on render would poll every connection for every file.
  return useMutation({
    mutationFn: (id: number) => fetchProcessingWhyHeld(id),
  });
}

/** One file's processing record, read again whenever the file itself changes. */
export function useProcessingFileLogQuery(fileId: number, updatedAt: string) {
  return useQuery({
    queryKey: processingKeys.fileLog(fileId, updatedAt),
    queryFn: () => fetchProcessingFileLog(fileId),
  });
}

export function useProcessingFileLog() {
  // A mutation rather than a query: a processing record is read when someone opens it,
  // not for every row on every render.
  return useMutation({
    mutationFn: (id: number) => fetchProcessingFileLog(id),
  });
}

export function useProcessingFileTracks() {
  // A mutation rather than a query: the tracks come from a fresh ffprobe of the source, done
  // only when an operator opens "Choose tracks" for one file, not for every row on every render.
  return useMutation({
    mutationFn: (id: number) => fetchProcessingFileTracks(id),
  });
}

export function useSubmitProcessingManualPlan() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      id,
      choice,
    }: {
      id: number;
      choice: ProcessingManualPlanChoice;
    }) => postProcessingManualPlan(id, choice),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useProcessProcessingFileNow() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      relative_media_path,
      media_scope,
      library_id,
      pass_through_unchanged,
    }: {
      relative_media_path: string;
      media_scope: "movie" | "tv";
      library_id?: number;
      pass_through_unchanged?: boolean;
    }) =>
      postProcessingFileRemuxPassEnqueue({
        relative_media_path,
        media_scope,
        library_id,
        pass_through_unchanged,
      }),
    onSuccess: () =>
      void qc.invalidateQueries({ queryKey: processingKeys.files }),
  });
}

export function useProcessingCheckLibraryAgain() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      media_scope,
      library_id,
    }: {
      media_scope: "movie" | "tv";
      library_id: number;
    }) =>
      postProcessingWatchedFolderRemuxScanDispatchEnqueue({
        enqueue_remux_jobs: true,
        media_scope,
        library_id,
      }),
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: processingKeys.files });
      void qc.invalidateQueries({ queryKey: processingKeys.jobs });
    },
  });
}
