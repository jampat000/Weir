import { apiFetch, readJson, requireOk } from "../api/client";
import { sendJson } from "../api/send-json";

/** One file kept without processing again from History's remove dialog (#785). */
export interface KeptFile {
  id: number;
  library_id: number;
  library_name: string;
  relative_path: string;
  size_bytes: number;
  kept_at: string;
}

export interface KeptFilesPage {
  files: KeptFile[];
}

const keptFilesPath = () => "/api/v1/processing/kept-files";

export async function fetchKeptFiles(): Promise<KeptFilesPage> {
  const path = keptFilesPath();
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not load kept files");
  return readJson<KeptFilesPage>(response);
}

export interface ProcessKeptFileAgainResult {
  detail: string;
}

/** Clears the marker and queues its library's watched folder to be looked at again. */
export async function processKeptFileAgain(
  id: number,
): Promise<ProcessKeptFileAgainResult> {
  const path = `${keptFilesPath()}/${id}/process-again`;
  const response = await sendJson(
    path,
    "POST",
    {},
    "Could not process that file again",
  );
  return readJson<ProcessKeptFileAgainResult>(response);
}
