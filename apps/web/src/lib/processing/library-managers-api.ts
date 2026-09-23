import { apiFetch, readJson, requireOk } from "../api/client";
import { sendJson } from "../api/send-json";
import type { Schema } from "../api/types";
import {
  processingLibrariesPath,
  type ProcessingLibrary,
  type ProcessingMediaType,
} from "./libraries-api";

export interface DiscoverableProcessingLibrary {
  key: string;
  name: string;
  media_type: ProcessingMediaType | null;
  root_path: string | null;
  already_imported: boolean;
  local_path_problem: string | null;
  processes_before_import: boolean;
  output_path: string | null;
  output_path_problem: string | null;
}

export interface ProcessingLibraryDrift {
  kind: "root_moved" | "library_removed" | "library_added" | "path_not_local";
  library_id: number | null;
  library_name: string;
  manager_value: string | null;
  weir_value: string | null;
  detail: string;
}

export async function discoverProcessingLibraries(
  connectionId: number,
): Promise<DiscoverableProcessingLibrary[]> {
  const path = `${processingLibrariesPath()}/discover/${connectionId}`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not ask that media manager for its libraries",
  );
  return readJson<DiscoverableProcessingLibrary[]>(response);
}

export async function importDiscoveredProcessingLibraries(
  connectionId: number,
  keys: string[],
): Promise<ProcessingLibrary[]> {
  const path = `${processingLibrariesPath()}/discover/${connectionId}/import`;
  const response = await sendJson(
    path,
    "POST",
    { keys },
    "Could not import those libraries",
  );
  return readJson<ProcessingLibrary[]>(response);
}

export async function fetchProcessingLibraryDrift(
  connectionId: number,
): Promise<ProcessingLibraryDrift[]> {
  const path = `${processingLibrariesPath()}/discover/${connectionId}/drift`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not compare libraries with that media manager",
  );
  return readJson<ProcessingLibraryDrift[]>(response);
}

export async function unlinkDiscoveredProcessingLibrary(
  id: number,
): Promise<ProcessingLibrary> {
  const path = `${processingLibrariesPath()}/${id}/unlink`;
  const response = await sendJson(
    path,
    "POST",
    {},
    "Could not unlink that library",
  );
  return readJson<ProcessingLibrary>(response);
}

/** Whether Reject can be chosen for a library linked to these managers, and why. */
export type ProcessingRejectSupport = Schema<"RejectSupportOut">;

export async function fetchProcessingRejectSupport(
  connectionIds: number[],
): Promise<ProcessingRejectSupport> {
  const query = new URLSearchParams();
  for (const id of connectionIds) query.append("connection_ids", String(id));
  const suffix = query.toString();
  const path = `/api/v1/processing/reject-support${suffix ? `?${suffix}` : ""}`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not check whether Reject is available",
  );
  return readJson<ProcessingRejectSupport>(response);
}

/**
 * What each connected Sonarr, Radarr or Deluno needs for a library with these folders, and whether it
 * already has it. Read only on the manager's side: Weir only ever sends it GET requests.
 */
export type ProcessingManagerSetup = Schema<"ManagerSetupOut">;
export type ProcessingManagerSetupItem = Schema<"ManagerSetupItemOut">;

export async function fetchProcessingManagerSetup(
  mediaType: ProcessingMediaType,
  watchedFolder: string,
  outputFolder: string,
  removeOriginal = true,
): Promise<ProcessingManagerSetup> {
  const query = new URLSearchParams({
    media_type: mediaType,
    watched_folder: watchedFolder,
    output_folder: outputFolder,
    remove_original_after_success: removeOriginal ? "true" : "false",
  });
  const path = `/api/v1/processing/manager-setup?${query.toString()}`;
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not check your media managers");
  return readJson<ProcessingManagerSetup>(response);
}
