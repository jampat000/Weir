import { apiFetch, readJson, requireOk } from "../api/client";
import type { ProcessingManagerSetupItem } from "./library-managers-api";

/**
 * #768's folder-chain check: Weir's own watched/work/output folders, plus every connected media manager's own setup
 * check (reused as-is from {@link ProcessingManagerSetupItem}), folded into one read-only, plain-language view. Not in
 * the generated OpenAPI types, so hand-written here to match the server's `LibraryFolderChainCheck` response exactly.
 */
export interface FolderChainLine {
  state: "ok" | "problem" | "note";
  text: string;
}

export interface LibraryFolderChainLocal {
  ready: boolean;
  lines: FolderChainLine[];
}

export interface LibraryFolderChain {
  library_id: number;
  local: LibraryFolderChainLocal;
  managers: ProcessingManagerSetupItem[];
  ready: boolean;
}

export async function fetchLibraryFolderChain(
  libraryId: number,
): Promise<LibraryFolderChain> {
  const path = `/api/v1/processing/libraries/${libraryId}/folder-chain`;
  const response = await apiFetch(path);
  await requireOk(path, response, "Could not check this library's folder chain");
  return readJson<LibraryFolderChain>(response);
}

export async function fetchConnectionFolderChain(
  connectionId: number,
): Promise<LibraryFolderChain[]> {
  const path = `/api/v1/media-managers/connections/${connectionId}/folder-chain`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not check the folder chain of the libraries linked to it",
  );
  return readJson<LibraryFolderChain[]>(response);
}
