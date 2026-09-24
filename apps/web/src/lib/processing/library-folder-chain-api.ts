import { apiFetch, readJson, requireOk } from "../api/client";
import type { Schema } from "../api/types";

/**
 * #768's folder-chain check: Weir's own watched/work/output folders, plus every connected media manager's own setup
 * check (reused as-is from {@link ProcessingManagerSetupItem} in library-managers-api), folded into one read-only,
 * plain-language view.
 */
export type FolderChainLine = Schema<"ManagerSetupLineOut">;
export type LibraryFolderChainLocal = Schema<"LibraryFolderChainLocalOut">;
export type LibraryFolderChain = Schema<"LibraryFolderChainOut">;

export async function fetchLibraryFolderChain(
  libraryId: number,
): Promise<LibraryFolderChain> {
  const path = `/api/v1/processing/libraries/${libraryId}/folder-chain`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not check this library's folder chain",
  );
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
