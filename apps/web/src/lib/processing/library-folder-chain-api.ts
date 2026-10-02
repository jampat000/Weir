import { apiFetch, readJson, requireOk } from "../api/client";
import type { Schema } from "../api/types";
import type { StatusMeaning } from "../ui/status-meaning";

/**
 * The folder-chain check: Weir's own watched/work/output folders, plus every connected media manager's own setup
 * check (reused as-is from {@link ProcessingManagerSetupItem} in library-managers-api), folded into one read-only,
 * plain-language view.
 */
export type FolderChainLine = Schema<"ManagerSetupLineOut">;
export type LibraryFolderChainLocal = Schema<"LibraryFolderChainLocalOut">;
export type LibraryFolderChainDownloadClient =
  Schema<"LibraryFolderChainDownloadClientOut">;
export type LibraryFolderChain = Schema<"LibraryFolderChainOut">;

export type Readiness = "ready" | "not_verified" | "needs_attention";

/**
 * "Ready" only when nothing is a problem and Weir verified every line it could not take on someone's word: a check
 * whose lines include an unverified one has not been proven, so it never shows the green "Ready".
 */
export function readinessOf(
  ready: boolean,
  lines: readonly FolderChainLine[],
): Readiness {
  if (!ready) return "needs_attention";
  return lines.some((line) => line.state === "unverified")
    ? "not_verified"
    : "ready";
}

export const READINESS_LABELS: Record<Readiness, string> = {
  ready: "Ready",
  not_verified: "Not verified",
  needs_attention: "Needs attention",
};

export const READINESS_MEANING: Record<Readiness, StatusMeaning> = {
  ready: "done",
  not_verified: "attention",
  needs_attention: "attention",
};

export const FOLDER_LINE_MEANING: Record<
  FolderChainLine["state"],
  StatusMeaning
> = {
  ok: "done",
  problem: "attention",
  note: "idle",
  unverified: "attention",
};

export async function fetchLibraryFolderChain(
  libraryId: number,
): Promise<LibraryFolderChain> {
  const path = `/api/v1/processing/libraries/${libraryId}/folder-chain`;
  const response = await apiFetch(path);
  await requireOk(
    path,
    response,
    "Could not check this workflow's folder chain",
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
    "Could not check the folder chain of the workflows linked to it",
  );
  return readJson<LibraryFolderChain[]>(response);
}

/** Every line of a chain: Weir's own folders, then each media manager's and each download client's. */
export function folderChainLines(chain: LibraryFolderChain): FolderChainLine[] {
  return [
    ...chain.local.lines,
    ...chain.managers.flatMap((manager) => manager.lines),
    ...chain.download_clients.flatMap((client) => client.lines),
  ];
}
