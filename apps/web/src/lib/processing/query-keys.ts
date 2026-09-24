import type { ProcessingFilesQuery } from "./files-api";
import type { ProcessingJobsInspectionFilter } from "./jobs-inspection/queries";
import type { ProcessingMediaType } from "./libraries-api";
import type { LibraryFileFilters } from "./library-mode-api";

/**
 * Every Processing query key. Each panel still reads its own key and loads on its own; sharing the
 * factory only means an invalidation can never miss the key it means.
 */
export const processingKeys = {
  /** Every processing-file query: the lists, and one file's log. */
  files: ["processing", "files"] as const,
  fileList: (query: ProcessingFilesQuery) =>
    ["processing", "files", query] as const,
  fileLog: (fileId: number, updatedAt: string) =>
    ["processing", "files", fileId, "log", updatedAt] as const,
  filesAtOnce: ["processing", "files-at-once"] as const,
  jobs: ["processing", "jobs"] as const,
  jobsInspection: ["processing", "jobs", "inspection"] as const,
  jobsInspectionList: (filter: ProcessingJobsInspectionFilter, limit: number) =>
    ["processing", "jobs", "inspection", filter, limit] as const,
  overviewStats: (windowDays?: number) =>
    windowDays === undefined
      ? (["processing", "overview-stats"] as const)
      : (["processing", "overview-stats", windowDays] as const),
  operatorSettings: ["processing", "operator-settings"] as const,
  runtimeSettings: ["processing", "runtime-settings"] as const,
  maintenance: ["processing", "maintenance"] as const,
  metadataProvider: ["processing", "metadata-provider"] as const,
  directPlayDevices: ["processing", "direct-play-devices"] as const,
  libraries: ["processing", "libraries"] as const,
  ruleSets: ["processing", "rule-sets"] as const,
  rejectSupport: (connectionIds: number[]) =>
    ["processing", "reject-support", ...connectionIds] as const,
  managerSetup: (
    mediaType: ProcessingMediaType,
    watchedFolder: string,
    outputFolder: string,
    removeOriginal: boolean,
  ) =>
    [
      "processing",
      "manager-setup",
      mediaType,
      watchedFolder,
      outputFolder,
      removeOriginal,
    ] as const,
  librarySettings: (libraryId: number) =>
    ["processing", "library-settings", libraryId] as const,
  /** Every page of one library's files, whatever the filters. */
  libraryFiles: (libraryId: number) =>
    ["processing", "library-files", libraryId] as const,
  libraryFileList: (libraryId: number, filters: LibraryFileFilters) =>
    ["processing", "library-files", libraryId, filters] as const,
  libraryOverview: (libraryId: number) =>
    ["processing", "library-overview", libraryId] as const,
  libraryFilePreview: (libraryId: number, path: string) =>
    ["processing", "library-file-preview", libraryId, path] as const,
};
